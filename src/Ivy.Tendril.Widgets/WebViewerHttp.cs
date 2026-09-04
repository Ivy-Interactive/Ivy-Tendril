using System.Net;
using System.Runtime.CompilerServices;

// WebViewerHttp is the gate every caller-named URL goes through, so it is tested directly.
// Spelled out here rather than as an <InternalsVisibleTo> item: this project sets
// GenerateAssemblyInfo=false, and the item only ever reaches the generated AssemblyInfo.
[assembly: InternalsVisibleTo("Ivy.Tendril.Test")]

namespace Ivy.Tendril.Widgets;

/// <summary>
/// The one way the WebViewer fetches a URL a caller named, and the reason the shared
/// <c>HttpClient</c> follows no redirects of its own.
///
/// A redirect is a second URL, chosen by the upstream rather than by us, and
/// <see cref="WebViewerProxyOptions.IsUrlAllowed"/> is only ever consulted on the first
/// one. With the handler following redirects, an allow-list that permits a developer's
/// own <c>localhost:3000</c> still fetches whatever that server answers a request with —
/// the cloud metadata endpoint included — and hands the body back. Following them here
/// puts every hop through the same gate.
///
/// Method rewriting matches what a browser does: 303, and 301/302 on a non-idempotent
/// method, continue as a bodiless GET; 307/308 preserve method and body.
/// </summary>
internal static class WebViewerHttp
{
    /// <summary>Hops past this are a redirect loop, whatever the upstream believes.</summary>
    public const int MaxRedirects = 10;

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        Uri uri,
        byte[]? body,
        Action<HttpRequestMessage>? configure,
        Func<Uri, bool>? isUrlAllowed,
        HttpCompletionOption completion,
        CancellationToken ct)
    {
        var currentMethod = method;
        var currentUri = uri;
        var currentBody = body;

        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(currentMethod, currentUri);
            if (currentBody is not null) request.Content = new ByteArrayContent(currentBody);
            configure?.Invoke(request);

            var response = await client.SendAsync(request, completion, ct);
            // Callers read the final URL back off the response — it is what a proxied page's
            // relative URLs resolve against — so make sure the last hop is the one recorded.
            response.RequestMessage ??= request;
            var status = response.StatusCode;
            if (hop >= MaxRedirects || !IsRedirect(status)) return response;

            var location = response.Headers.Location;
            if (location is null) return response;

            var next = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps) return response;

            if (isUrlAllowed is not null && !isUrlAllowed(next))
            {
                response.Dispose();
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent($"Redirect to {next.GetLeftPart(UriPartial.Authority)} blocked by IsUrlAllowed"),
                    RequestMessage = new HttpRequestMessage(currentMethod, currentUri),
                };
            }

            response.Dispose();

            var idempotent = currentMethod == HttpMethod.Get || currentMethod == HttpMethod.Head;
            if (status is HttpStatusCode.SeeOther ||
                (status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found && !idempotent))
            {
                currentMethod = HttpMethod.Get;
                currentBody = null;
            }

            currentUri = next;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently      // 301
            or HttpStatusCode.Found                    // 302
            or HttpStatusCode.SeeOther                 // 303
            or HttpStatusCode.TemporaryRedirect        // 307
            or HttpStatusCode.PermanentRedirect;       // 308
}

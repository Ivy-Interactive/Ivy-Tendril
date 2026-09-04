using System.Net;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Test.Widgets;

// The proxy fetches URLs its caller names, so IsUrlAllowed is the only thing standing between
// it and the rest of the network. A redirect is a URL the CALLER did not name, which is why
// the handler follows none of them and WebViewerHttp follows them one gated hop at a time.
public class WebViewerHttpTests
{
    private sealed class Router(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, bool HasBody)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var hasBody = request.Content is not null && (await request.Content.ReadAsByteArrayAsync(ct)).Length > 0;
            Seen.Add((request.Method, request.RequestUri!.AbsoluteUri, hasBody));
            return handler(request);
        }
    }

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location) =>
        new(status) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private static HttpResponseMessage Ok(string body = "ok") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static Task<HttpResponseMessage> Send(
        Router router, Func<Uri, bool>? isUrlAllowed, HttpMethod? method = null, byte[]? body = null) =>
        WebViewerHttp.SendAsync(
            new HttpClient(router), method ?? HttpMethod.Get, new Uri("https://start.test/a"), body,
            null, isUrlAllowed, HttpCompletionOption.ResponseContentRead, CancellationToken.None);

    [Fact]
    public async Task Follows_A_Redirect_And_Reports_The_Final_Url()
    {
        var router = new Router(req =>
            req.RequestUri!.AbsoluteUri == "https://start.test/a"
                ? Redirect(HttpStatusCode.Found, "https://start.test/b")
                : Ok());

        using var response = await Send(router, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://start.test/b", response.RequestMessage!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Resolves_A_Relative_Location_Against_The_Hop_It_Came_From()
    {
        var router = new Router(req =>
            req.RequestUri!.AbsoluteUri == "https://start.test/a" ? Redirect(HttpStatusCode.Found, "/b") : Ok());

        using var response = await Send(router, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://start.test/b", router.Seen[1].Url);
    }

    // The whole point: an allow-list that only lets the viewer reach a dev server must not be
    // walked past by that server answering with a redirect to somewhere else.
    [Fact]
    public async Task Blocks_A_Redirect_To_A_Url_The_Allow_List_Rejects()
    {
        var router = new Router(_ => Redirect(HttpStatusCode.Found, "http://169.254.169.254/latest/meta-data/"));

        using var response = await Send(router, uri => uri.Host == "start.test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(router.Seen); // the second hop was never sent
    }

    [Fact]
    public async Task Allows_A_Redirect_The_Allow_List_Accepts()
    {
        var router = new Router(req =>
            req.RequestUri!.AbsoluteUri == "https://start.test/a"
                ? Redirect(HttpStatusCode.Found, "https://start.test/b")
                : Ok());

        using var response = await Send(router, uri => uri.Host == "start.test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(302)] // a POST that a browser would continue as a GET
    [InlineData(303)]
    public async Task Continues_A_Posted_Redirect_As_A_Bodiless_Get(int status)
    {
        var router = new Router(req =>
            req.RequestUri!.AbsoluteUri == "https://start.test/a"
                ? Redirect((HttpStatusCode)status, "https://start.test/b")
                : Ok());

        using var response = await Send(router, null, HttpMethod.Post, "payload"u8.ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpMethod.Get, router.Seen[1].Method);
        Assert.False(router.Seen[1].HasBody);
    }

    [Fact]
    public async Task Keeps_Method_And_Body_Across_A_307()
    {
        var router = new Router(req =>
            req.RequestUri!.AbsoluteUri == "https://start.test/a"
                ? Redirect(HttpStatusCode.TemporaryRedirect, "https://start.test/b")
                : Ok());

        using var response = await Send(router, null, HttpMethod.Post, "payload"u8.ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpMethod.Post, router.Seen[1].Method);
        Assert.True(router.Seen[1].HasBody);
    }

    [Fact]
    public async Task Gives_Up_On_A_Redirect_Loop()
    {
        var router = new Router(_ => Redirect(HttpStatusCode.Found, "https://start.test/a"));

        using var response = await Send(router, null);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(WebViewerHttp.MaxRedirects + 1, router.Seen.Count);
    }
}

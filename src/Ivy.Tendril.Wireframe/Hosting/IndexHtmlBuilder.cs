using System.Net;
using System.Text;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Project;

namespace Ivy.Tendril.Wireframe.Hosting;

/// <summary>
/// Injects everything the page needs into the user's index.html: stylesheets, the import
/// map, the bundle, and (in serve mode) the live-reload client.
///
/// The user's index.html stays editable and is never rewritten on disk: this happens on
/// the way out of the server.
/// </summary>
public sealed class IndexHtmlBuilder(VendorManifest vendor)
{
    /// <param name="site">The wireframe being served.</param>
    /// <param name="siteBase">
    /// The wireframe's own address, ending in a slash: <c>/</c> for the standalone server,
    /// <c>/__wireframes/123/checkout/</c> for a plan preview.
    /// </param>
    /// <param name="payloadBase">Where the shared payload is served, ending in a slash.</param>
    public string Build(WireframeSite site, string siteBase = "/", string payloadBase = "/__wireframe/")
    {
        var project = site.Project;
        var html = File.Exists(project.IndexHtml)
            ? File.ReadAllText(project.IndexHtml)
            : ScaffoldTemplates.IndexHtml.Replace("{{TITLE}}", project.Name);

        // Stylesheet order is load-bearing. tendril.css declares
        // @layer properties, theme, base, utilities first, which fixes that order for the
        // whole document; the utility sheet then emits into the same theme/utilities layers
        // and must come after so it wins on equal specificity. fonts.css is last so its
        // @font-face rules beat any that a stray Google Fonts sheet might contribute.
        var utilities = site.UtilityCssPath is not null
            ? $"{siteBase}__wireframe/utilities.css"
            : $"{payloadBase}css/wireframe-utilities.css";

        var head = new StringBuilder();
        foreach (var href in new[] { $"{payloadBase}css/tendril.css", utilities, $"{payloadBase}css/fonts.css" })
            head.Append($"    <link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(href)}\">\n");

        head.Append("    <script type=\"importmap\">\n");
        head.Append(vendor.ToImportMapJson(payloadBase));
        head.Append("\n    </script>\n");

        var encodedBase = WebUtility.HtmlEncode(siteBase);
        var body = new StringBuilder();
        body.Append($"    <script type=\"module\" src=\"{encodedBase}__wireframe/out/bundle.js\"></script>\n");
        if (site.LiveReload)
            body.Append($"    <script src=\"{encodedBase}__wireframe/client.js\" data-base=\"{encodedBase}\"></script>\n");

        html = InsertBefore(html, "</head>", head.ToString());
        html = InsertBefore(html, "</body>", body.ToString());
        return html;
    }

    /// <summary>
    /// Inserts before a closing tag, appending if the document does not have one. Keeps a
    /// hand-edited index.html working even if the user removed the tag.
    /// </summary>
    private static string InsertBefore(string html, string tag, string insert)
    {
        var index = html.LastIndexOf(tag, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? html + insert : html.Insert(index, insert);
    }
}

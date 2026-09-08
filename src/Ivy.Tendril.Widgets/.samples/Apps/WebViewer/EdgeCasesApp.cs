using Ivy;
using Ivy.Tendril.Widgets;
using WebViewerWidget = Ivy.Tendril.Widgets.WebViewer;

namespace WidgetSamples.Apps.WebViewer;

// The chrome under stress: a URL that cannot fit, no URL at all, a server that is not there,
// a 404, a page that takes seconds to answer, a pane too narrow for comfort, and a toolbar
// crowded with host actions in every state. Each card is one viewer.
[App(title: "Edge cases", icon: Icons.TriangleAlert, group: ["WebViewer"])]
public class EdgeCasesApp : ViewBase
{
    public override object Build()
    {
        var site = UseService<DemoSiteAddress>();

        static object Case(string title, string note, object viewer) =>
            new Card(Layout.Vertical().Gap(2) | Text.Muted(note) | viewer).Title(title);

        static WebViewerWidget Viewer(string? url, int height = 320) =>
            new WebViewerWidget().Url(url).Toolbar().Width(Size.Full()).Height(Size.Px(height));

        var crowded = Viewer(site.Page(DemoSite.TasksPath))
            .Actions(
                new WebViewerAction("draw", WebViewerIcon.Pencil, "Draw on the page") { Active = true },
                new WebViewerAction("shot", WebViewerIcon.Camera, "Take a screenshot"),
                new WebViewerAction("bug", WebViewerIcon.Bug, "Report a bug (disabled until the page loads)") { Disabled = true },
                new WebViewerAction("open", WebViewerIcon.ExternalLink, "Open in a new tab"),
                new WebViewerAction("update", WebViewerIcon.MessageSquare, "Send 120 comments to the agent")
                {
                    Badge = "99+",
                    Primary = true,
                });

        var narrow = new WebViewerWidget()
            .Url(site.Page(DemoSite.SettingsPath))
            .Device(WebViewerDevice.Mobile)
            .Toolbar()
            .Actions(new WebViewerAction("update", WebViewerIcon.MessageSquare, "Send 3 comments to the agent")
            {
                Badge = "3",
                Primary = true,
            })
            .Width(Size.Px(360))
            .Height(Size.Px(420));

        return Layout.Grid().Columns(2).Gap(4)
            | Case("Long URL", "The address pill truncates; hover it for the full URL, click to edit.",
                Viewer(site.Page(DemoSite.LongPath)))
            | Case("Query string route", "Settings tabs are separate URLs; the query stays visible in the pill.",
                Viewer(site.Page(DemoSite.SettingsPath)))
            | Case("No URL", "Nothing loaded yet: the pill shows a placeholder, Back and Forward are off.",
                Viewer(null))
            | Case("Unreachable server", "Nothing listens on port 1; the proxy's error page comes through the frame.",
                Viewer("http://localhost:1/"))
            | Case("Not found", "A 404 from the app under review keeps the URL that was asked for.",
                Viewer(site.Page(DemoSite.MissingPath)))
            | Case("Slow page", "Three seconds to first byte: the progress line runs until the page lands.",
                Viewer(site.Page(DemoSite.SlowPath)))
            | Case("Crowded toolbar", "Every host action state at once: active, disabled, primary with a wide badge.",
                crowded)
            | Case("Narrow pane, mobile viewport", "360px wide: the pill gives way first and the icon groups keep their size.",
                narrow);
    }
}

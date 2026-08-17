using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;
using WebViewerWidget = Ivy.Tendril.Widgets.WebViewer;

namespace WidgetSamples.Apps.WebViewer;

// A full web-inspector application built entirely in Ivy on top of the WebViewer widget.
// The widget is just the proxied iframe (+ comment overlay); everything else here — the
// toolbar, device switch, and Console/Network/Captures panels — is native Ivy UI driven
// by the widget's typed OnEvent firehose and its Commands stream.
[App(title: "Inspector", icon: Icons.Globe, group: ["WebViewer"])]
public class DemoApp : ViewBase
{
    public override object Build()
    {
        const string home = "https://ivy.app";

        var commands = UseStream<WebViewerCommand>();
        var address = UseState(home);   // editable address-bar text
        var currentUrl = UseState(home); // value bound to the widget's Url prop
        var device = UseState(WebViewerDevice.Desktop);
        var events = UseState(ImmutableList<WebViewerEvent>.Empty);
        var activeTab = UseState("console");
        var canGoBack = UseState(false);
        var canGoForward = UseState(false);
        var selecting = UseState(false);
        var drawing = UseState(false);

        // ---- the widget -----------------------------------------------------
        var viewer = new WebViewerWidget()
            .Url(currentUrl.Value)
            .Device(device.Value)
            .Commands(commands)
            .WithOnEvent(e =>
            {
                events.Set(prev => prev.Add(e));
                switch (e)
                {
                    case NavigateEvent nav:
                        address.Set(nav.Url);
                        currentUrl.Set(nav.Url); // widget ignores this (matches current page)
                        canGoBack.Set(nav.CanGoBack);
                        canGoForward.Set(nav.CanGoForward);
                        break;
                    case CommentEvent:
                        selecting.Set(false); // the agent stops select mode after a pick
                        break;
                }
            })
            .Width(Size.Full())
            .Height(Size.Full());

        // ---- toolbar --------------------------------------------------------
        Button DeviceBtn(string label, WebViewerDevice d)
        {
            var b = new Button(label).Small().OnClick(() => device.Set(d));
            return device.Value == d ? b.Primary() : b;
        }

        var toggleSelect = new Button(selecting.Value ? "Selecting…" : "Select").Small()
            .OnClick(() =>
            {
                var next = !selecting.Value;
                selecting.Set(next);
                commands.Write(new SelectModeCommand(next));
                if (next && drawing.Value) { drawing.Set(false); commands.Write(new DrawModeCommand(false)); }
            });
        if (selecting.Value) toggleSelect = toggleSelect.Primary();

        var toggleDraw = new Button(drawing.Value ? "Drawing" : "Draw").Small()
            .OnClick(() =>
            {
                var next = !drawing.Value;
                drawing.Set(next);
                commands.Write(new DrawModeCommand(next));
                if (next && selecting.Value) { selecting.Set(false); commands.Write(new SelectModeCommand(false)); }
            });
        if (drawing.Value) toggleDraw = toggleDraw.Primary();

        var toolbar = Layout.Horizontal().Gap(1).Width(Size.Full())
            | new Button("←").Small().Disabled(!canGoBack.Value).OnClick(() => commands.Write(new BackCommand()))
            | new Button("→").Small().Disabled(!canGoForward.Value).OnClick(() => commands.Write(new ForwardCommand()))
            | new Button("⟳").Small().OnClick(() => commands.Write(new ReloadCommand()))
            | address.ToTextInput().Placeholder("Enter a URL").Width(Size.Full())
                .OnSubmit(_ => currentUrl.Set(address.Value)) // Enter navigates
            | new Button("Go").Small().Primary().OnClick(() => currentUrl.Set(address.Value))
            | DeviceBtn("Desktop", WebViewerDevice.Desktop)
            | DeviceBtn("Mobile", WebViewerDevice.Mobile)
            | DeviceBtn("Tablet", WebViewerDevice.Tablet)
            | toggleSelect
            | toggleDraw
            | new Button("Screenshot").Small().OnClick(() => commands.Write(new CaptureCommand("viewport")));

        // ---- dev-tools panel -----------------------------------------------
        var all = events.Value;
        var consoleItems = all.Where(IsConsoleLike).ToImmutableList();
        var netItems = all.OfType<HttpEvent>().ToImmutableList();
        var capItems = all.OfType<CaptureEvent>().ToImmutableList();

        Button Tab(string key, string label, int count)
        {
            var b = new Button($"{label} ({count})").Small().Ghost().OnClick(() => activeTab.Set(key));
            return activeTab.Value == key ? b.Primary() : b;
        }

        var tabs = Layout.Horizontal().Gap(1)
            | Tab("console", "Console", consoleItems.Count)
            | Tab("network", "Network", netItems.Count)
            | Tab("captures", "Captures", capItems.Count)
            | new Button("Clear").Small().Ghost().Destructive()
                .OnClick(() => events.Set(ImmutableList<WebViewerEvent>.Empty));

        object panelBody = activeTab.Value switch
        {
            "network" => RenderNetwork(netItems),
            "captures" => RenderCaptures(capItems),
            _ => RenderConsole(consoleItems),
        };

        // Dev-tools tabs in their own header; the active panel body scrolls below.
        var panel = new HeaderLayout(tabs, panelBody).Height(Size.Full());

        // Toolbar in the header; the viewport (70%) and dev-tools panel (30%) share a
        // vertical resizable split.
        return new HeaderLayout(
            toolbar,
            new ResizablePanelGroup(
                new ResizablePanel(Size.Fraction(0.70f), viewer),
                new ResizablePanel(Size.Fraction(0.30f), panel)
            ).Vertical()
        ).Scroll(Scroll.None);
    }

    // ---- rendering helpers ----------------------------------------------------
    private static bool IsConsoleLike(WebViewerEvent e) =>
        e is ConsoleEvent or ClickEvent or CommentEvent;

    private static object RenderConsole(ImmutableList<WebViewerEvent> items)
    {
        if (items.Count == 0) return Text.Muted("No messages");
        var rows = Recent(items).Select(e => (object)RenderConsoleRow(e));
        return Layout.Vertical().Gap(1) | rows;
    }

    private static object RenderConsoleRow(WebViewerEvent e)
    {
        switch (e)
        {
            case ConsoleEvent c:
                {
                    var text = Text.Block($"{c.Level}: {c.Text}");
                    if (c.Level == "error") text = text.Color(Colors.Red);
                    else if (c.Level == "warn") text = text.Color(Colors.Amber);
                    return text;
                }
            case ClickEvent c:
                return Text.Block($"🖱 {c.Tag} {(c.Text is { Length: > 0 } ? $"“{c.Text}” " : "")}· {c.Selector}").Color(Colors.Muted);
            case CommentEvent c:
                return Text.Block($"💬 {c.Comment}  ·  {c.Tag} {c.Selector}").Color(Colors.Blue);
            default:
                return Text.Block(e.ToString() ?? "");
        }
    }

    private record NetworkRow(string Name, string Method, string Status, string Type, string Size, string Time);

    private static object RenderNetwork(ImmutableList<HttpEvent> items)
    {
        if (items.Count == 0) return Text.Muted("No requests");
        var rows = Recent(items)
            .Select(h => new NetworkRow(
                HarName(h.Url),
                h.Method,
                h.Status == 0 ? "failed" : h.Status.ToString(),
                string.IsNullOrEmpty(h.ResourceType) ? "—" : h.ResourceType,
                FmtSize(h.Size),
                $"{h.Time} ms"))
            .ToList();
        return rows.AsQueryable().ToDataTable();
    }

    private static object RenderCaptures(ImmutableList<CaptureEvent> items)
    {
        if (items.Count == 0) return Text.Muted("No captures — click Screenshot");
        var rows = Recent(items).Select(c => (object)(
            Layout.Vertical().Gap(0)
            | Text.Block($"{c.Mode} · {c.Width}×{c.Height}")
            | Text.Muted(c.Path)
        ));
        return Layout.Vertical().Gap(2) | rows;
    }

    // Newest first, capped so the panel stays light.
    private static IEnumerable<T> Recent<T>(IReadOnlyList<T> items)
    {
        var take = Math.Min(items.Count, 100);
        for (var i = items.Count - 1; i >= items.Count - take; i--)
            yield return items[i];
    }

    private static string HarName(string url)
    {
        try
        {
            var u = new Uri(url);
            var seg = u.Segments.LastOrDefault()?.Trim('/');
            return (string.IsNullOrEmpty(seg) ? u.Host : seg) + (string.IsNullOrEmpty(u.Query) ? "" : u.Query);
        }
        catch { return url; }
    }

    private static string FmtSize(long bytes)
    {
        if (bytes < 0) return "—";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }
}

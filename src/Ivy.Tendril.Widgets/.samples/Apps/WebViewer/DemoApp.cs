using System.Collections.Immutable;
using System.Text.Json;
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
        // Comments are the one event stream that is not append-only: a pin can be edited or
        // removed from inside the page, so the live set is kept apart from the raw log.
        var comments = UseState(ImmutableList<CommentEvent>.Empty);
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
                    case CommentEvent c:
                        comments.Set(prev => prev.Add(c));
                        selecting.Set(false); // the agent stops select mode after a pick
                        break;
                    case CommentUpdatedEvent u:
                        comments.Set(prev => prev
                            .Select(x => x.Id == u.Id ? x with { Comment = u.Comment } : x)
                            .ToImmutableList());
                        break;
                    case CommentDeletedEvent d:
                        // Numbers are positions, so the remaining pins renumber themselves —
                        // in the page and here. Keeping arrival order is all it takes.
                        comments.Set(prev => prev
                            .RemoveAll(x => x.Id == d.Id)
                            .Select((x, i) => x with { Number = i + 1 })
                            .ToImmutableList());
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
        // One tab per event kind in the OnEvent firehose, so each payload is rendered
        // with the columns that actually matter for it.
        var all = events.Value;
        var consoleItems = all.OfType<ConsoleEvent>().ToImmutableList();
        var clickItems = all.OfType<ClickEvent>().ToImmutableList();
        var commentItems = comments.Value;
        var drawItems = all.OfType<DrawEvent>().ToImmutableList();
        var navItems = all.OfType<NavigateEvent>().ToImmutableList();
        var netItems = all.OfType<HttpEvent>().ToImmutableList();
        var capItems = all.OfType<CaptureEvent>().ToImmutableList();

        Button Tab(string key, string label, int count)
        {
            var b = new Button($"{label} ({count})").Small().Ghost().OnClick(() => activeTab.Set(key));
            return activeTab.Value == key ? b.Primary() : b;
        }

        var tabs = Layout.Horizontal().Gap(1)
            | Tab("console", "Console", consoleItems.Count)
            | Tab("clicks", "Clicks", clickItems.Count)
            | Tab("comments", "Comments", commentItems.Count)
            | Tab("draw", "Draw", drawItems.Count)
            | Tab("navigate", "Navigate", navItems.Count)
            | Tab("network", "Network", netItems.Count)
            | Tab("captures", "Captures", capItems.Count)
            // The log only. The comments are still pinned in the page, and a panel that
            // disagreed with what the viewer is showing would be worse than a full log.
            | new Button("Clear").Small().Ghost().Destructive()
                .OnClick(() => events.Set(ImmutableList<WebViewerEvent>.Empty));

        // Keyed by tab. The table tabs render structurally identical subtrees — a data table in
        // the same slot — so without a distinct key Ivy reconciles them as the same view and
        // the first table's columns and rows stay on screen whichever tab is selected. The
        // vertical layout doubles as the flex parent a data table needs in order to grow.
        object panelBody = (Layout.Vertical().Height(Size.Full()) | (object)(activeTab.Value switch
        {
            "clicks" => RenderClicks(clickItems),
            "comments" => RenderComments(commentItems),
            "draw" => RenderDraw(drawItems),
            "navigate" => RenderNavigate(navItems),
            "network" => RenderNetwork(netItems),
            "captures" => RenderCaptures(capItems),
            _ => RenderConsole(consoleItems),
        })).Key(activeTab.Value);

        // Dev-tools tabs in their own header. Tabs showing a data table turn the header's own
        // scrolling off: with it on, the content wrapper is sized to its content, so a table
        // asking for Size.Full() has no height to fill and collapses to a few rows. The table
        // scrolls itself. The list-style tabs keep the header's scrolling.
        var showsTable = activeTab.Value is "clicks" or "draw" or "navigate" or "network";
        var panel = new HeaderLayout(tabs, panelBody).Height(Size.Full());
        if (showsTable) panel = panel.Scroll(Scroll.None);

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
    private static object RenderConsole(ImmutableList<ConsoleEvent> items)
    {
        if (items.Count == 0) return Text.Muted("No messages");
        var rows = Recent(items).Select(c =>
        {
            var text = Text.Block($"{c.Level}: {c.Text}");
            if (c.Level == "error") text = text.Color(Colors.Red);
            else if (c.Level == "warn") text = text.Color(Colors.Amber);
            return (object)text;
        });
        return Layout.Vertical().Gap(1) | rows;
    }

    private record ClickRow(string Tag, string Text, string Selector, string Button, string Position, string Source);

    private static object RenderClicks(ImmutableList<ClickEvent> items)
    {
        if (items.Count == 0) return Text.Muted("No clicks — click anywhere in the page");
        var rows = Recent(items)
            .Select(c => new ClickRow(
                c.Tag,
                string.IsNullOrEmpty(c.Text) ? "—" : c.Text,
                c.Selector,
                ButtonName(c.Button),
                $"{c.X:0}, {c.Y:0}",
                ComponentName(c.DebugJson)))
            .ToList();
        return rows.AsQueryable().ToDataTable().Width(Size.Full());
    }

    // The live set, in pin order — not the event log. Editing or deleting a pin in the page
    // rewrites this list, which is the whole point of the id/number pair on the events.
    private static object RenderComments(ImmutableList<CommentEvent> items)
    {
        if (items.Count == 0) return Text.Muted("No comments — press Select, then pick an element");
        var rows = items.Select(c => (object)(
            Layout.Vertical().Gap(0)
            | Text.Block($"{c.Number}. {c.Comment}").Color(Colors.Blue)
            | Text.Muted($"{c.Tag} · {c.Selector} · {ComponentName(c.DebugJson)}")
            | Text.Muted(c.Xpath)
        ));
        return Layout.Vertical().Gap(2) | rows;
    }

    private record DrawRow(string Points, string Start, string End, string Element);

    private static object RenderDraw(ImmutableList<DrawEvent> items)
    {
        if (items.Count == 0) return Text.Muted("No strokes — press Draw and drag over the page");
        var rows = Recent(items)
            .Select(d =>
            {
                // PointsJson is the raw per-point array the injected agent produced;
                // unpacking it here is what a real consumer would do with the stroke.
                var pts = ParsePoints(d.PointsJson);
                var first = pts.FirstOrDefault();
                var last = pts.LastOrDefault();
                return new DrawRow(
                    d.PointCount.ToString(),
                    first is null ? "—" : $"{first.X}, {first.Y}",
                    last is null ? "—" : $"{last.X}, {last.Y}",
                    string.IsNullOrEmpty(first?.Selector) ? "—" : first.Selector);
            })
            .ToList();
        return rows.AsQueryable().ToDataTable().Width(Size.Full());
    }

    private record NavigateRow(string Url, string Back, string Forward);

    private static object RenderNavigate(ImmutableList<NavigateEvent> items)
    {
        if (items.Count == 0) return Text.Muted("No navigations");
        var rows = Recent(items)
            .Select(n => new NavigateRow(n.Url, YesNo(n.CanGoBack), YesNo(n.CanGoForward)))
            .ToList();
        return rows.AsQueryable().ToDataTable().Width(Size.Full());
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
        return rows.AsQueryable().ToDataTable().Width(Size.Full());
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

    private static string ButtonName(int button) => button switch
    {
        0 => "left",
        1 => "middle",
        2 => "right",
        _ => button.ToString(),
    };

    private static string YesNo(bool value) => value ? "yes" : "no";

    // The widget hands attribution over as a JSON string (see ClickEvent.DebugJson). Show
    // the resolved source location when there is one, else the owning component name —
    // whichever tier answered, that is what a reviewer recognises.
    private static string ComponentName(string? debugJson)
    {
        if (string.IsNullOrEmpty(debugJson)) return "—";
        try
        {
            using var doc = JsonDocument.Parse(debugJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object
                && source.TryGetProperty("file", out var file) && file.GetString() is { Length: > 0 } path)
            {
                var line = source.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number
                    ? $":{l.GetInt32()}" : "";
                return $"{path}{line}";
            }

            if (root.TryGetProperty("ownerChain", out var owners) && owners.ValueKind == JsonValueKind.Array
                && owners.GetArrayLength() > 0
                && owners[0].TryGetProperty("name", out var name))
            {
                return name.GetString() ?? "—";
            }
        }
        catch (JsonException) { /* not a payload we understand */ }
        return "—";
    }

    private sealed record DrawPoint(int X, int Y, string? Selector);

    private static readonly JsonSerializerOptions PointsJsonOptions = new(JsonSerializerDefaults.Web);

    private static IReadOnlyList<DrawPoint> ParsePoints(string pointsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<DrawPoint>>(pointsJson, PointsJsonOptions) ?? [];
        }
        catch (JsonException) { return []; }
    }
}

namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilShell",
    GlobalName = "IvyTendrilWidgets"
)]
[Slot("SidebarHeader")]
[Slot("SidebarBody")]
[Slot("SidebarFooter")]
[Slot("Content")]
[Slot("SessionContents")]
[Slot("Tabs")]
public record TendrilShell : WidgetBase<TendrilShell>
{
    public TendrilShell(
        object? sidebarHeader = null,
        object?[]? sidebarBody = null,
        object? sidebarFooter = null,
        object? content = null,
        object?[]? sessionContents = null,
        object? tabs = null)
        : base(BuildSlots(sidebarHeader, sidebarBody, sidebarFooter, content, sessionContents, tabs))
    {
    }

    private static object[] BuildSlots(
        object? sidebarHeader, object?[]? sidebarBody, object? sidebarFooter,
        object? content, object?[]? sessionContents, object? tabs) =>
    [
        sidebarHeader != null ? new Slot("SidebarHeader", sidebarHeader) : new Slot("SidebarHeader"),
        new Slot("SidebarBody", (sidebarBody ?? []).Where(c => c != null).Cast<object>().ToArray()),
        sidebarFooter != null ? new Slot("SidebarFooter", sidebarFooter) : new Slot("SidebarFooter"),
        content != null ? new Slot("Content", content) : new Slot("Content"),
        new Slot("SessionContents", (sessionContents ?? []).Where(c => c != null).Cast<object>().ToArray()),
        tabs != null ? new Slot("Tabs", tabs) : new Slot("Tabs")
    ];

    [Prop] public bool Collapsed { get; init; }

    /// <summary>Index into the SessionContents children of the visible session pane; null shows Content.</summary>
    [Prop] public int? ActiveSessionIndex { get; init; }

    [Event] public EventHandler<Event<TendrilShell, bool>>? OnCollapsedChanged { get; init; }
}

public static class TendrilShellExtensions
{
    public static TendrilShell Collapsed(this TendrilShell w, bool collapsed) =>
        w with { Collapsed = collapsed };

    public static TendrilShell ActiveSessionIndex(this TendrilShell w, int? index) =>
        w with { ActiveSessionIndex = index };

    public static TendrilShell OnCollapsedChanged(this TendrilShell w, Action<bool> handler) =>
        w with { OnCollapsedChanged = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };
}

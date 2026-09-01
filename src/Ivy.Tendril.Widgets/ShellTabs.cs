namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ShellTabs",
    GlobalName = "IvyTendrilWidgets"
)]
public record ShellTabs : WidgetBase<ShellTabs>
{
    [Prop] public List<ShellTabDto> Tabs { get; init; } = new();
    [Prop] public string? SelectedId { get; init; }

    [Event] public EventHandler<Event<ShellTabs, string>>? OnSelect { get; init; }
    [Event] public EventHandler<Event<ShellTabs, string>>? OnClose { get; init; }
    [Event] public EventHandler<Event<ShellTabs>>? OnNew { get; init; }
}

public static class ShellTabsExtensions
{
    public static ShellTabs Tabs(this ShellTabs w, List<ShellTabDto> tabs) =>
        w with { Tabs = tabs };

    public static ShellTabs SelectedId(this ShellTabs w, string? selectedId) =>
        w with { SelectedId = selectedId };

    public static ShellTabs OnSelect(this ShellTabs w, Action<string> handler) =>
        w with { OnSelect = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };

    public static ShellTabs OnClose(this ShellTabs w, Action<string> handler) =>
        w with { OnClose = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };

    public static ShellTabs OnNew(this ShellTabs w, Action handler) =>
        w with { OnNew = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}

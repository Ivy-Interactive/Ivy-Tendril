namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ShellNav",
    GlobalName = "IvyTendrilWidgets"
)]
public record ShellNav : WidgetBase<ShellNav>
{
    [Prop] public List<ShellNavItemDto> Items { get; init; } = new();
    [Prop] public bool ShowDivider { get; init; }

    [Event] public EventHandler<Event<ShellNav, string>>? OnSelect { get; init; }
}

public static class ShellNavExtensions
{
    public static ShellNav Items(this ShellNav w, List<ShellNavItemDto> items) =>
        w with { Items = items };

    public static ShellNav ShowDivider(this ShellNav w, bool show = true) =>
        w with { ShowDivider = show };

    public static ShellNav OnSelect(this ShellNav w, Action<string> handler) =>
        w with { OnSelect = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };
}

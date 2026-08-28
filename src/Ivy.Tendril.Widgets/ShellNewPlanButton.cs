namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ShellNewPlanButton",
    GlobalName = "IvyTendrilWidgets"
)]
public record ShellNewPlanButton : WidgetBase<ShellNewPlanButton>
{
    [Prop] public string Label { get; init; } = "New Plan";

    /// <summary>Key combined with the platform command key (Cmd/Ctrl), handled client-side.</summary>
    [Prop] public string ShortcutKey { get; init; } = "K";

    [Event] public EventHandler<Event<ShellNewPlanButton>>? OnClick { get; init; }
}

public static class ShellNewPlanButtonExtensions
{
    public static ShellNewPlanButton Label(this ShellNewPlanButton w, string label) =>
        w with { Label = label };

    public static ShellNewPlanButton OnClick(this ShellNewPlanButton w, Action handler) =>
        w with { OnClick = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}

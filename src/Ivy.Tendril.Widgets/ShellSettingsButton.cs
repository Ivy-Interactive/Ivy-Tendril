namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ShellSettingsButton",
    GlobalName = "IvyTendrilWidgets"
)]
public record ShellSettingsButton : WidgetBase<ShellSettingsButton>
{
    [Prop] public string Label { get; init; } = "Settings";

    [Event] public EventHandler<Event<ShellSettingsButton>>? OnClick { get; init; }
}

public static class ShellSettingsButtonExtensions
{
    public static ShellSettingsButton Label(this ShellSettingsButton w, string label) =>
        w with { Label = label };

    public static ShellSettingsButton OnClick(this ShellSettingsButton w, Action handler) =>
        w with { OnClick = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}

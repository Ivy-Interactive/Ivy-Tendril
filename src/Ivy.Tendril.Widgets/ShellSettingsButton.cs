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

    /// <summary>Lucide icon name; the frontend bundles only the icons the sidebar footer hosts.</summary>
    [Prop] public string Icon { get; init; } = "Settings";

    /// <summary>When false the button is icon-only and shows its label as a tooltip.</summary>
    [Prop] public bool ShowLabel { get; init; } = true;

    [Prop] public bool IsActive { get; init; }

    [Event] public EventHandler<Event<ShellSettingsButton>>? OnClick { get; init; }
}

public static class ShellSettingsButtonExtensions
{
    public static ShellSettingsButton Label(this ShellSettingsButton w, string label) =>
        w with { Label = label };

    public static ShellSettingsButton Icon(this ShellSettingsButton w, string icon) =>
        w with { Icon = icon };

    public static ShellSettingsButton ShowLabel(this ShellSettingsButton w, bool showLabel) =>
        w with { ShowLabel = showLabel };

    public static ShellSettingsButton IsActive(this ShellSettingsButton w, bool isActive) =>
        w with { IsActive = isActive };

    public static ShellSettingsButton OnClick(this ShellSettingsButton w, Action handler) =>
        w with { OnClick = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}

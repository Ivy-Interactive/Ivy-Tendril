namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ShellSidebarHeader",
    GlobalName = "IvyTendrilWidgets"
)]
public record ShellSidebarHeader : WidgetBase<ShellSidebarHeader>
{
    [Prop] public string Title { get; init; } = "Ivy Tendril";
    [Prop] public string? Version { get; init; }
    [Prop] public string? LogoUrl { get; init; }
}

public static class ShellSidebarHeaderExtensions
{
    public static ShellSidebarHeader Title(this ShellSidebarHeader w, string title) =>
        w with { Title = title };

    public static ShellSidebarHeader Version(this ShellSidebarHeader w, string? version) =>
        w with { Version = version };

    public static ShellSidebarHeader LogoUrl(this ShellSidebarHeader w, string? url) =>
        w with { LogoUrl = url };
}

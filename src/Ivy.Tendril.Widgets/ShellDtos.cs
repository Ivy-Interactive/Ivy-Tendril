namespace Ivy.Tendril.Widgets;

public record ShellNavItemDto(string Id, string Label, string? Icon = null, string? Badge = null, bool IsActive = false);

public record ShellBadgeDto(string Label, string Kind = "neutral", string? Color = null)
{
    public static ShellBadgeDto Project(string label) => new(label, "project");
    public static ShellBadgeDto Success(string label) => new(label, "success");
    public static ShellBadgeDto Warning(string label) => new(label, "warning");
    public static ShellBadgeDto Colored(string label, Colors color) => new(label, "color", color.ToString());
}

public record ShellSectionItemDto(string Id, string Title, string? Tag = null, List<ShellBadgeDto>? Badges = null, string? Icon = null, string? State = null);

public record ShellTabDto(string Id, string Title);

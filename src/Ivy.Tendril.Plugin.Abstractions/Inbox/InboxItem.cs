namespace Ivy.Plugins.Inbox;

/// <summary>
/// A structured item to be added to the Tendril Inbox for plan creation.
/// </summary>
public record InboxItem
{
    /// <summary>
    /// The task description. This becomes the body of the inbox markdown file
    /// and is passed to the CreatePlan promptware as the task to plan.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Target project name, or "Auto" to let Tendril detect the project from context.
    /// Must match a project name in config.yaml.
    /// </summary>
    public string Project { get; init; } = "Auto";

    /// <summary>URL linking back to the source (e.g., GitHub issue, Linear issue).</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Short identifier from the source system (e.g., "#123", "IVY-456").</summary>
    public string? SourceIdentifier { get; init; }

    /// <summary>Optional labels for categorization. Written to frontmatter.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}

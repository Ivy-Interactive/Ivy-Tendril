namespace Ivy.Plugins.Messaging;

public record Message
{
    public required MessageContent Content { get; init; }
    public string? ThreadId { get; init; }
    public IReadOnlyList<FileAttachment>? Attachments { get; init; }
}

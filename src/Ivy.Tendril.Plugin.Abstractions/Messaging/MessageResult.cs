namespace Ivy.Plugins.Messaging;

public record MessageResult
{
    public required string MessageId { get; init; }
    public required string ThreadId { get; init; }
    public required string Channel { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

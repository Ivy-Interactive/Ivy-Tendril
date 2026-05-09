namespace Ivy.Plugins.Messaging;

public record FileAttachment(byte[] Content, string FileName, string? Title = null);

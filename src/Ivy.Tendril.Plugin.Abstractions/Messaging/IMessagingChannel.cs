namespace Ivy.Plugins.Messaging;

/// <summary>
/// Contract for messaging platform integrations.
///
/// COMPATIBILITY NOTE: All future methods added to this interface MUST provide
/// a default implementation (DIM) to avoid breaking existing plugin implementations.
/// Plugins implement this interface directly, so adding abstract members is a breaking change.
/// </summary>
public interface IMessagingChannel
{
    string Platform { get; }
    string? DefaultChannel { get; }

    Task<MessageResult> SendMessageAsync(
        string channel,
        Message message,
        CancellationToken ct = default);

    Task DeleteMessageAsync(
        string channel,
        string messageId,
        CancellationToken ct = default);

    Task<MessageResult> UploadFileAsync(
        string channel,
        Stream content,
        string fileName,
        string? title = null,
        string? threadId = null,
        CancellationToken ct = default);
}

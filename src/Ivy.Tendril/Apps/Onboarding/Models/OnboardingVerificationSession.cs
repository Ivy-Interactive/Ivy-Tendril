using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding.Models;

public class OnboardingVerificationSession(
    IWriteStream<string> stream,
    IState<PromptwareRunHandle?> handle,
    IState<bool> hasOutput,
    IState<bool> running,
    IState<bool> started,
    IState<bool> cancelled,
    IState<string?> error,
    IState<int> refreshToken)
{
    public IWriteStream<string> Stream { get; } = stream;
    public IState<PromptwareRunHandle?> Handle { get; } = handle;
    public IState<bool> HasOutput { get; } = hasOutput;
    public IState<bool> Running { get; } = running;
    public IState<bool> Started { get; } = started;
    public IState<bool> Cancelled { get; } = cancelled;
    public IState<string?> Error { get; } = error;
    public IState<int> RefreshToken { get; } = refreshToken;

    public void Reset()
    {
        Handle.Value?.Cancel();
        Handle.Set(null);
        HasOutput.Set(false);
        Running.Set(false);
        Started.Set(false);
        Cancelled.Set(false);
        Error.Set(null);
    }
}

internal class NotifyingStream<T>(IWriteStream<T> inner, Action onFirstWrite) : IWriteStream<T>
{
    private bool _notified;

    public string Id => inner.Id;

    public void Write(T data)
    {
        if (!_notified && data is string json)
        {
            var trimmed = json.TrimStart();
            if (!trimmed.StartsWith("{\"type\":\"system\"") && !trimmed.StartsWith("{\"type\":\"user\"") &&
                !trimmed.StartsWith("{\"kind\":\"session_init\""))
            {
                _notified = true;
                onFirstWrite();
            }
        }
        inner.Write(data);
    }
}

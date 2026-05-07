using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public class OnboardingVerificationSession
{
    public IWriteStream<string> Stream { get; }
    public IState<PromptwareRunHandle?> Handle { get; }
    public IState<bool> HasOutput { get; }
    public IState<bool> Running { get; }
    public IState<bool> Started { get; }
    public IState<bool> Cancelled { get; }
    public IState<string?> Error { get; }
    public IState<int> RefreshToken { get; }

    public OnboardingVerificationSession(
        IWriteStream<string> stream,
        IState<PromptwareRunHandle?> handle,
        IState<bool> hasOutput,
        IState<bool> running,
        IState<bool> started,
        IState<bool> cancelled,
        IState<string?> error,
        IState<int> refreshToken)
    {
        Stream = stream;
        Handle = handle;
        HasOutput = hasOutput;
        Running = running;
        Started = started;
        Cancelled = cancelled;
        Error = error;
        RefreshToken = refreshToken;
    }

    public void Reset()
    {
        Handle.Value?.Cancel();
        Handle.Set((PromptwareRunHandle?)null);
        HasOutput.Set(false);
        Running.Set(false);
        Started.Set(false);
        Cancelled.Set(false);
        Error.Set((string?)null);
    }
}

internal class NotifyingStream<T> : IWriteStream<T>
{
    private readonly IWriteStream<T> _inner;
    private readonly Action _onFirstWrite;
    private bool _notified;

    public NotifyingStream(IWriteStream<T> inner, Action onFirstWrite)
    {
        _inner = inner;
        _onFirstWrite = onFirstWrite;
    }

    public string Id => _inner.Id;

    public void Write(T data)
    {
        if (!_notified && data is string json)
        {
            var trimmed = json.TrimStart();
            if (!trimmed.StartsWith("{\"type\":\"system\"") && !trimmed.StartsWith("{\"type\":\"user\""))
            {
                _notified = true;
                _onFirstWrite();
            }
        }
        _inner.Write(data);
    }
}

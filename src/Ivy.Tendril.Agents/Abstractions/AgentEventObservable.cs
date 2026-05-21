using System.Reactive.Linq;

namespace Ivy.Tendril.Agents.Abstractions;

public static class AgentEventObservable
{
    public static IDisposable OnSessionStarting(this IObservable<AgentEvent> events, Action<SessionStartingEvent> handler)
        => events.OfType<SessionStartingEvent>().Subscribe(handler);

    public static IDisposable OnSessionActive(this IObservable<AgentEvent> events, Action<SessionActiveEvent> handler)
        => events.OfType<SessionActiveEvent>().Subscribe(handler);

    public static IDisposable OnSessionCompleted(this IObservable<AgentEvent> events, Action<SessionCompletedEvent> handler)
        => events.OfType<SessionCompletedEvent>().Subscribe(handler);

    public static IDisposable OnIdleTimeout(this IObservable<AgentEvent> events, Action<IdleTimeoutEvent> handler)
        => events.OfType<IdleTimeoutEvent>().Subscribe(handler);

    public static IDisposable OnRetry(this IObservable<AgentEvent> events, Action<RetryEvent> handler)
        => events.OfType<RetryEvent>().Subscribe(handler);

    public static IDisposable OnText(this IObservable<AgentEvent> events, Action<TextEvent> handler)
        => events.OfType<TextEvent>().Subscribe(handler);

    public static IDisposable OnThinking(this IObservable<AgentEvent> events, Action<ThinkingEvent> handler)
        => events.OfType<ThinkingEvent>().Subscribe(handler);

    public static IDisposable OnToolCall(this IObservable<AgentEvent> events, Action<ToolCallEvent> handler)
        => events.OfType<ToolCallEvent>().Subscribe(handler);

    public static IDisposable OnToolResult(this IObservable<AgentEvent> events, Action<ToolResultEvent> handler)
        => events.OfType<ToolResultEvent>().Subscribe(handler);

    public static IDisposable OnError(this IObservable<AgentEvent> events, Action<ErrorEvent> handler)
        => events.OfType<ErrorEvent>().Subscribe(handler);

    public static IDisposable OnResult(this IObservable<AgentEvent> events, Action<ResultEvent> handler)
        => events.OfType<ResultEvent>().Subscribe(handler);

    public static IDisposable OnFileChange(this IObservable<AgentEvent> events, Action<FileChangeEvent> handler)
        => events.OfType<FileChangeEvent>().Subscribe(handler);

    public static IDisposable OnPermissionRequest(this IObservable<AgentEvent> events, Action<PermissionRequestEvent> handler)
        => events.OfType<PermissionRequestEvent>().Subscribe(handler);

    public static IDisposable OnUserQuestion(this IObservable<AgentEvent> events, Action<UserQuestionEvent> handler)
        => events.OfType<UserQuestionEvent>().Subscribe(handler);
}

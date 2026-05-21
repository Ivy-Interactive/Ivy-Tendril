using System.Reactive.Linq;
using System.Reactive.Subjects;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class TestSessionBuilder
{
    private readonly List<AgentEvent> _events = [];
    private ResultEvent? _result;
    private string _agentId = AgentId.Claude;
    private string? _sessionId;
    private SessionMetadata? _metadata;

    public TestSessionBuilder WithAgentId(string agentId)
    {
        _agentId = agentId;
        return this;
    }

    public TestSessionBuilder WithSessionId(string sessionId)
    {
        _sessionId = sessionId;
        return this;
    }

    public TestSessionBuilder WithMetadata(SessionMetadata metadata)
    {
        _metadata = metadata;
        return this;
    }

    public TestSessionBuilder AddText(string text)
    {
        _events.Add(new TextEvent { Kind = AgentEventKind.Text, Text = text });
        return this;
    }

    public TestSessionBuilder AddThinking(string content)
    {
        _events.Add(new ThinkingEvent { Kind = AgentEventKind.Thinking, Content = content });
        return this;
    }

    public TestSessionBuilder AddToolCall(string toolName, string? inputJson = null)
    {
        _events.Add(new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = $"toolu_{Guid.NewGuid():N}"[..20],
            ToolName = toolName,
            InputJson = inputJson,
        });
        return this;
    }

    public TestSessionBuilder AddToolResult(string toolUseId, string? output = null, bool isError = false)
    {
        _events.Add(new ToolResultEvent
        {
            Kind = AgentEventKind.ToolResult,
            ToolUseId = toolUseId,
            Output = output,
            IsError = isError,
        });
        return this;
    }

    public TestSessionBuilder AddError(string message, bool isRetryable = false, bool isAuthError = false)
    {
        _events.Add(new ErrorEvent
        {
            Kind = AgentEventKind.Error,
            Message = message,
            IsRetryable = isRetryable,
            IsAuthError = isAuthError,
        });
        return this;
    }

    public TestSessionBuilder AddFileChange(string filePath, FileChangeKind changeKind, int linesAdded = 0, int linesRemoved = 0)
    {
        _events.Add(new FileChangeEvent
        {
            Kind = AgentEventKind.FileChange,
            FilePath = filePath,
            ChangeKind = changeKind,
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved,
        });
        return this;
    }

    public TestSessionBuilder AddEvent(AgentEvent evt)
    {
        _events.Add(evt);
        return this;
    }

    public TestSessionBuilder WithResult(bool isSuccess, string? response = null, AgentUsage? usage = null)
    {
        _result = new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = isSuccess,
            Response = response,
            Usage = usage,
            ExitCode = isSuccess ? 0 : 1,
        };
        return this;
    }

    public IAgentSession Build()
    {
        var result = _result ?? new ResultEvent
        {
            Kind = AgentEventKind.Result,
            IsSuccess = true,
            ExitCode = 0,
        };

        return new CompletedTestSession(
            _sessionId ?? Guid.NewGuid().ToString("N"),
            _agentId,
            _events,
            result,
            _metadata);
    }

    private sealed class CompletedTestSession : IAgentSession
    {
        private readonly ReplaySubject<AgentEvent> _events = new();

        public string SessionId { get; }
        public string AgentId { get; }
        public SessionState State => SessionState.Completed;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? CompletedAt { get; }
        public SessionMetadata? Metadata { get; }
        public IObservable<AgentEvent> Events => _events.AsObservable();
        public IObservable<string>? RawOutput => null;
        public IObservable<string>? RawStderr => null;
        public ResultEvent? Result { get; }
        public bool SupportsPermissionResponse => false;
        public bool SupportsQuestionResponse => false;
        public bool SupportsMultiTurn => false;

        public CompletedTestSession(
            string sessionId,
            string agentId,
            IReadOnlyList<AgentEvent> events,
            ResultEvent result,
            SessionMetadata? metadata)
        {
            SessionId = sessionId;
            AgentId = agentId;
            Result = result;
            Metadata = metadata;
            CompletedAt = DateTimeOffset.UtcNow;

            foreach (var evt in events)
                _events.OnNext(evt);
            _events.OnNext(result);
            _events.OnCompleted();
        }

        public Task<ResultEvent> WaitForCompletionAsync(CancellationToken ct = default)
            => Task.FromResult(Result!);

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task KillAsync() => Task.CompletedTask;

        public Task RespondToPermissionAsync(string requestId, PermissionDecision decision, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RespondToQuestionAsync(string questionId, QuestionResponse response, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendFollowUpAsync(string message, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            _events.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

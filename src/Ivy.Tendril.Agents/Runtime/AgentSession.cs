using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class AgentSession : IAgentSession
{
    private readonly Process _process;
    private readonly IEventParser _parser;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IInteractionHandler? _interactionHandler;
    private readonly TimeSpan? _idleTimeout;
    private readonly ReplaySubject<AgentEvent> _events = new();
    private readonly ReplaySubject<string> _rawOutput = new();
    private readonly ReplaySubject<string> _rawStderr = new();
    private readonly TaskCompletionSource<ResultEvent> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<AgentEvent> _allEvents = [];
    private volatile SessionState _state = SessionState.NotStarted;
    private long _lastActivityTicks;
    private bool _idleTimeoutFired;

    public string SessionId { get; }
    public string AgentId { get; }
    public SessionState State => _state;
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public SessionMetadata? Metadata { get; init; }
    public IObservable<AgentEvent> Events => _events.AsObservable();
    public IObservable<string>? RawOutput => _rawOutput.AsObservable();
    public IObservable<string>? RawStderr => _rawStderr.AsObservable();
    public ResultEvent? Result { get; private set; }
    public bool SupportsPermissionResponse => _interactionHandler is not null;
    public bool SupportsQuestionResponse => _interactionHandler is not null;
    public bool SupportsMultiTurn => false;

    internal AgentSession(
        Process process,
        IEventParser parser,
        string agentId,
        string? sessionId,
        ILogger? logger = null,
        TimeProvider? timeProvider = null,
        IInteractionHandler? interactionHandler = null,
        TimeSpan? idleTimeout = null)
    {
        _process = process;
        _parser = parser;
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interactionHandler = interactionHandler;
        _idleTimeout = idleTimeout;
        AgentId = agentId;
        SessionId = sessionId ?? Guid.NewGuid().ToString("N");
        StartedAt = _timeProvider.GetUtcNow();
        _lastActivityTicks = Stopwatch.GetTimestamp();
    }

    internal async Task StartAsync(AgentProcessSpec spec, CancellationToken ct)
    {
        _state = SessionState.Starting;

        if (spec.StdinContent is not null)
        {
            await ProcessRunner.WriteStdinAndCloseAsync(_process, spec.StdinContent, ct);
        }

        _state = SessionState.Running;

        _ = Task.Run(() => ReadStderrAsync(_cts.Token), _cts.Token);

        if (_idleTimeout.HasValue)
            _ = Task.Run(() => MonitorIdleAsync(_idleTimeout.Value, _cts.Token), _cts.Token);

        await ReadOutputAsync(_cts.Token);
    }

    private async Task ReadOutputAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var line in ProcessRunner.ReadLinesAsync(_process.StandardOutput, ct))
            {
                MarkActivity();
                AgentLogMessages.RawLineReceived(_logger, SessionId, line.Length);

                _rawOutput.OnNext(line);

                var events = _parser.ParseLine(line);

                if (events.Count > 0)
                {
                    AgentLogMessages.LinesParsed(_logger, SessionId, events.Count);
                    foreach (var evt in events)
                    {
                        _allEvents.Add(evt);
                        _events.OnNext(evt);
                        await HandleInteractionAsync(evt, ct);
                    }
                }
            }

            var flushed = _parser.Flush();
            foreach (var evt in flushed)
            {
                _allEvents.Add(evt);
                _events.OnNext(evt);
            }

            await _process.WaitForExitAsync(ct);
            Complete(_process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            var state = _idleTimeoutFired ? SessionState.Failed : SessionState.Stopped;
            Complete(-1, state);
        }
        catch (Exception ex)
        {
            _events.OnNext(new ErrorEvent
            {
                Kind = AgentEventKind.Error,
                Message = ex.Message,
            });
            Complete(-1, SessionState.Failed);
        }
    }

    private async Task HandleInteractionAsync(AgentEvent evt, CancellationToken ct)
    {
        if (_interactionHandler is null) return;

        var interactionContext = new InteractionContext
        {
            SessionId = SessionId,
            AgentId = AgentId,
            Metadata = Metadata,
        };

        switch (evt)
        {
            case PermissionRequestEvent permReq:
                var decision = await _interactionHandler.HandlePermissionAsync(permReq, interactionContext, ct);
                AgentLogMessages.PermissionHandled(_logger, SessionId, permReq.ToolName, decision?.Granted ?? false);

                if (decision is null)
                {
                    _state = SessionState.Blocked;
                }
                break;

            case UserQuestionEvent question:
                var response = await _interactionHandler.HandleQuestionAsync(question, interactionContext, ct);
                AgentLogMessages.QuestionHandled(_logger, SessionId, question.QuestionId, response?.IsCancelled ?? true);

                if (response is null)
                {
                    _state = SessionState.Blocked;
                }
                break;
        }
    }

    private async Task MonitorIdleAsync(TimeSpan idleTimeout, CancellationToken ct)
    {
        var checkInterval = idleTimeout < TimeSpan.FromSeconds(10)
            ? TimeSpan.FromMilliseconds(500)
            : TimeSpan.FromSeconds(2);

        try
        {
            while (!ct.IsCancellationRequested && _state is SessionState.Running or SessionState.Starting)
            {
                await Task.Delay(checkInterval, ct);

                var elapsed = Stopwatch.GetElapsedTime(_lastActivityTicks);
                if (elapsed >= idleTimeout)
                {
                    _idleTimeoutFired = true;

                    AgentLogMessages.IdleTimeoutFired(_logger, SessionId, (long)elapsed.TotalMilliseconds);

                    var idleEvent = new IdleTimeoutEvent
                    {
                        Kind = AgentEventKind.IdleTimeout,
                        SessionId = SessionId,
                        IdleDuration = elapsed,
                    };
                    _allEvents.Add(idleEvent);
                    _events.OnNext(idleEvent);

                    await _cts.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void MarkActivity()
    {
        _lastActivityTicks = Stopwatch.GetTimestamp();
    }

    private async Task ReadStderrAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var line in ProcessRunner.ReadLinesAsync(_process.StandardError, ct))
            {
                MarkActivity();
                _rawStderr.OnNext(line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // ignored
        }
        finally
        {
            _rawStderr.OnCompleted();
        }
    }

    private void Complete(int exitCode, SessionState? overrideState = null)
    {
        CompletedAt = _timeProvider.GetUtcNow();

        var result = _parser.BuildResult(_allEvents, exitCode);
        Result = result;

        if (result is not null)
        {
            _allEvents.Add(result);
            _events.OnNext(result);
        }

        _state = overrideState ?? (exitCode == 0 ? SessionState.Completed : SessionState.Failed);

        var durationMs = (long)(CompletedAt.Value - StartedAt).TotalMilliseconds;
        AgentLogMessages.SessionCompleted(_logger, SessionId, _state, durationMs);

        if (exitCode != 0 && overrideState is not SessionState.Stopped)
            AgentLogMessages.ProcessCrashed(_logger, SessionId, exitCode);

        _events.OnCompleted();
        _rawOutput.OnCompleted();

        if (result is not null)
            _completion.TrySetResult(result);
        else
            _completion.TrySetResult(new ResultEvent
            {
                Kind = AgentEventKind.Result,
                IsSuccess = false,
                ExitCode = exitCode,
            });
    }

    internal bool IdleTimeoutFired => _idleTimeoutFired;
    internal IReadOnlyList<AgentEvent> AllEvents => _allEvents;

    public Task<ResultEvent> WaitForCompletionAsync(CancellationToken ct = default)
    {
        if (ct == CancellationToken.None)
            return _completion.Task;

        return WaitWithCancellationAsync(ct);
    }

    private async Task<ResultEvent> WaitWithCancellationAsync(CancellationToken ct)
    {
        using var reg = ct.Register(() => _completion.TrySetCanceled(ct));
        return await _completion.Task;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_state is SessionState.Completed or SessionState.Failed or SessionState.Stopped)
            return;

        ProcessRunner.SendInterrupt(_process);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await _process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            ProcessRunner.KillProcessTree(_process);
        }

        await _cts.CancelAsync();
    }

    public async Task KillAsync()
    {
        ProcessRunner.KillProcessTree(_process);
        await _cts.CancelAsync();

        try
        {
            await _completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { }
    }

    public Task RespondToPermissionAsync(string requestId, PermissionDecision decision, CancellationToken ct = default)
    {
        if (_interactionHandler is null)
            throw new NotSupportedException("This session does not support interactive permission responses.");
        return Task.CompletedTask;
    }

    public Task RespondToQuestionAsync(string questionId, QuestionResponse response, CancellationToken ct = default)
    {
        if (_interactionHandler is null)
            throw new NotSupportedException("This session does not support interactive question responses.");
        return Task.CompletedTask;
    }

    public Task SendFollowUpAsync(string message, CancellationToken ct = default)
        => throw new NotSupportedException("CLI sessions do not support multi-turn follow-ups.");

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        try
        {
            await _completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch { }

        _cts.Dispose();

        if (!_process.HasExited)
        {
            ProcessRunner.KillProcessTree(_process);
        }

        _process.Dispose();
        _events.Dispose();
        _rawOutput.Dispose();
        _rawStderr.Dispose();
    }
}

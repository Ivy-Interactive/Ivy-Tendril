using Ivy.Tendril.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Agents.Runtime;

public static partial class AgentLogMessages
{
    [LoggerMessage(Level = LogLevel.Trace, Message = "Raw line received for session {SessionId}: {LineLength} chars")]
    public static partial void RawLineReceived(ILogger logger, string sessionId, int lineLength);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Parsed {EventCount} events from line for session {SessionId}")]
    public static partial void LinesParsed(ILogger logger, string sessionId, int eventCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} launching agent {AgentId}")]
    public static partial void SessionLaunching(ILogger logger, string sessionId, string agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} started for agent {AgentId}")]
    public static partial void SessionStarted(ILogger logger, string sessionId, string agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session {SessionId} completed: {FinalState} in {DurationMs}ms")]
    public static partial void SessionCompleted(ILogger logger, string sessionId, SessionState finalState, long durationMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Health check for {AgentId}: installed={IsInstalled}, version={Version}")]
    public static partial void HealthCheckResult(ILogger logger, string agentId, bool isInstalled, string? version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idle timeout fired for session {SessionId} after {IdleDurationMs}ms")]
    public static partial void IdleTimeoutFired(ILogger logger, string sessionId, long idleDurationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unparseable line in session {SessionId}: {LinePreview}")]
    public static partial void UnparseableLine(ILogger logger, string sessionId, string linePreview);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Concurrency limit reached, queuing session {SessionId}")]
    public static partial void ConcurrencyLimitReached(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Process for session {SessionId} crashed with exit code {ExitCode}")]
    public static partial void ProcessCrashed(ILogger logger, string sessionId, int exitCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to launch agent {AgentId}: {ErrorMessage}")]
    public static partial void LaunchFailed(ILogger logger, string agentId, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Validation failed for agent {AgentId}: {ProblemCount} problem(s)")]
    public static partial void ValidationFailed(ILogger logger, string agentId, int problemCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrying session {SessionId} (attempt {Attempt}) after {DelayMs}ms")]
    public static partial void RetryingSession(ILogger logger, string sessionId, int attempt, long delayMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retry exhausted for session {SessionId} after {Attempt} attempts")]
    public static partial void RetryExhausted(ILogger logger, string sessionId, int attempt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Permission request for session {SessionId}: tool={ToolName}, granted={Granted}")]
    public static partial void PermissionHandled(ILogger logger, string sessionId, string toolName, bool granted);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Question handled for session {SessionId}: questionId={QuestionId}, cancelled={IsCancelled}")]
    public static partial void QuestionHandled(ILogger logger, string sessionId, string questionId, bool isCancelled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording session {SessionId} to {RecordingPath}")]
    public static partial void RecordingStarted(ILogger logger, string sessionId, string recordingPath);
}

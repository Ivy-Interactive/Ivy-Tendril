using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Agents.Runtime;

public sealed class AgentRunner(ILogger<AgentRunner> logger, ConcurrencyOptions? concurrency = null)
    : IAgentRunner
{
    private readonly Dictionary<string, IAgentCli> _clis = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IEventParser> _parsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IAgentHealthCheck> _healthChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IFailureAnalyzer> _failureAnalyzers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ISessionCostParser> _costParsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IAgentPty> _ptys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IModelCatalogProvider> _modelCatalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IAgentSession> _activeSessions = [];
    private readonly ReplaySubject<IAgentSession> _sessions = new();
    private readonly ConcurrencyLimiter? _concurrencyLimiter = concurrency is not null ? new ConcurrencyLimiter(concurrency) : null;
    private readonly AgentValidator _validator = new();

    public AgentRunner()
        : this(NullLogger<AgentRunner>.Instance, concurrency: null)
    {
    }

    public IReadOnlyList<IAgentSession> ActiveSessions
    {
        get
        {
            lock (_activeSessions)
                return _activeSessions.Where(s => s.State is SessionState.Running or SessionState.Starting).ToList();
        }
    }

    public IObservable<IAgentSession> Sessions => _sessions.AsObservable();
    public IReadOnlyList<string> RegisteredAgents => _clis.Keys.ToList();

    public AgentRunner Register(IAgentCli cli, IEventParser parser, IAgentHealthCheck healthCheck)
    {
        _clis[cli.Id] = cli;
        _parsers[cli.Id] = parser;
        _healthChecks[cli.Id] = healthCheck;
        return this;
    }

    public AgentRunner Register(
        IAgentCli cli,
        IEventParser parser,
        IAgentHealthCheck healthCheck,
        IFailureAnalyzer? failureAnalyzer = null,
        ISessionCostParser? costParser = null,
        IAgentPty? pty = null,
        IModelCatalogProvider? modelCatalog = null)
    {
        _clis[cli.Id] = cli;
        _parsers[cli.Id] = parser;
        _healthChecks[cli.Id] = healthCheck;
        if (failureAnalyzer is not null) _failureAnalyzers[cli.Id] = failureAnalyzer;
        if (costParser is not null) _costParsers[cli.Id] = costParser;
        if (pty is not null) _ptys[cli.Id] = pty;
        if (modelCatalog is not null) _modelCatalogs[cli.Id] = modelCatalog;
        return this;
    }

    public IFailureAnalyzer? GetFailureAnalyzer(string agentId)
        => _failureAnalyzers.GetValueOrDefault(agentId);

    public ISessionCostParser? GetCostParser(string agentId)
        => _costParsers.GetValueOrDefault(agentId);

    public IAgentPty? GetPty(string agentId)
        => _ptys.GetValueOrDefault(agentId);

    public IModelCatalogProvider? GetModelCatalog(string agentId)
        => _modelCatalogs.GetValueOrDefault(agentId);

    public IEnumerable<IModelCatalogProvider> ModelCatalogs => _modelCatalogs.Values;

    public IAgentHealthCheck GetHealthCheck(string agentId)
    {
        if (!_healthChecks.TryGetValue(agentId, out var hc))
            throw new ArgumentException($"No health check registered for agent '{agentId}'", nameof(agentId));
        return hc;
    }

    public IAgentDescriptor GetDescriptor(string agentId)
    {
        if (!_clis.TryGetValue(agentId, out var cli))
            throw new ArgumentException($"No descriptor registered for agent '{agentId}'", nameof(agentId));
        return cli;
    }

    public IAgentCli GetCli(string agentId)
    {
        if (!_clis.TryGetValue(agentId, out var cli))
            throw new ArgumentException($"No CLI registered for agent '{agentId}'", nameof(agentId));
        return cli;
    }

    public async Task<IAgentSession> LaunchAsync(AgentResolutionContext context, CancellationToken ct = default)
    {
        var agentId = context.AgentId ?? RegisteredAgents.First();
        var cli = GetCli(agentId);
        var parser = GetParser(agentId);
        var sessionId = context.SessionId ?? Guid.NewGuid().ToString("N");

        var problems = _validator.Validate(context, cli);
        var errors = problems.Where(p => p.Severity == ValidationSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            AgentLogMessages.ValidationFailed(logger, agentId, errors.Count);
            throw new AgentLaunchException(agentId,
                $"Validation failed: {string.Join("; ", errors.Select(e => e.Message))}");
        }

        IDisposable? concurrencyLease = null;
        if (_concurrencyLimiter is not null)
        {
            AgentLogMessages.ConcurrencyLimitReached(logger, sessionId);
            concurrencyLease = await _concurrencyLimiter.AcquireAsync(ct);
        }

        AgentLogMessages.SessionLaunching(logger, sessionId, agentId);

        var config = new AgentLaunchConfig
        {
            Prompt = context.Prompt,
            WorkingDirectory = context.WorkingDirectory,
            Model = context.ModelOverride,
            Effort = context.EffortOverride,
            SessionId = context.SessionId,
            PermissionMode = context.PermissionMode,
            AllowedTools = context.AllowedTools,
            DeniedTools = context.DeniedTools,
            ExtraArguments = context.ExtraArguments,
            EnvironmentVariables = context.ExtraEnvironment,
            MaxTurns = context.MaxTurns,
            MaxBudgetUsd = context.MaxBudgetUsd,
            McpServers = context.McpServers,
            PromptFilePath = context.PromptFilePath,
        };

        AgentProcessSpec spec;
        try
        {
            spec = cli.BuildProcessSpec(config);
        }
        catch (Exception ex)
        {
            concurrencyLease?.Dispose();
            AgentLogMessages.LaunchFailed(logger, agentId, ex.Message);
            throw new AgentLaunchException(agentId, $"Failed to build process spec: {ex.Message}", ex);
        }

        Process process;
        try
        {
            process = ProcessRunner.StartProcess(spec);
        }
        catch (Exception ex)
        {
            concurrencyLease?.Dispose();
            AgentLogMessages.LaunchFailed(logger, agentId, ex.Message);
            throw new AgentLaunchException(agentId, spec, ex);
        }

        var session = new AgentSession(
            process, parser, agentId, sessionId, logger,
            interactionHandler: context.InteractionHandler,
            idleTimeout: context.TimeoutPolicy?.IdleTimeout)
        {
            Metadata = context.Metadata,
        };

        IDisposable? recording = null;
        if (context.RecordingBasePath is not null)
        {
            recording = session.RecordTo(context.RecordingBasePath);
            AgentLogMessages.RecordingStarted(logger, sessionId,
                Path.Combine(context.RecordingBasePath, $"{sessionId}.jsonl"));
        }

        lock (_activeSessions)
            _activeSessions.Add(session);

        _sessions.OnNext(session);

        AgentLogMessages.SessionStarted(logger, sessionId, agentId);

        var timeout = context.TimeoutPolicy?.TotalTimeout;

        _ = Task.Run(async () =>
        {
            try
            {
                using var timeoutCts = timeout.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                    : null;
                timeoutCts?.CancelAfter(timeout!.Value);

                await session.StartAsync(spec, timeoutCts?.Token ?? ct);
            }
            catch (OperationCanceledException)
            {
                await session.KillAsync();
            }
            catch (Exception)
            {
                await session.KillAsync();
            }
            finally
            {
                recording?.Dispose();
                concurrencyLease?.Dispose();
            }
        }, CancellationToken.None);

        return session;
    }

    public async Task<ResultEvent> RunToCompletionAsync(AgentResolutionContext context, CancellationToken ct = default)
    {
        var retryPolicy = context.RetryPolicy;
        var agentId = context.AgentId ?? RegisteredAgents.First();
        var sessionId = context.SessionId ?? Guid.NewGuid().ToString("N");
        var startTime = Stopwatch.GetTimestamp();
        var attempt = 0;

        while (true)
        {
            var attemptContext = attempt == 0
                ? context
                : context with { SessionId = $"{sessionId}-r{attempt}" };

            await using var session = (AgentSession)await LaunchAsync(attemptContext, ct);
            var result = await session.WaitForCompletionAsync(ct);

            if (result.IsSuccess || retryPolicy is null)
                return result;

            var errorEvent = FindLastError(session) ?? new ErrorEvent
            {
                Kind = AgentEventKind.Error,
                Message = $"Process exited with code {result.ExitCode}",
                IsRetryable = true,
            };

            var elapsed = Stopwatch.GetElapsedTime(startTime);
            var retryContext = new RetryContext
            {
                Error = errorEvent,
                Attempt = attempt,
                Elapsed = elapsed,
                AgentId = agentId,
            };

            var decision = retryPolicy.ShouldRetry(retryContext);
            if (!decision.ShouldRetry)
            {
                AgentLogMessages.RetryExhausted(logger, sessionId, attempt + 1);
                return result;
            }

            attempt++;
            AgentLogMessages.RetryingSession(logger, sessionId, attempt, (long)decision.Delay.TotalMilliseconds);
            await Task.Delay(decision.Delay, ct);
        }
    }

    private static ErrorEvent? FindLastError(AgentSession session)
    {
        for (var i = session.AllEvents.Count - 1; i >= 0; i--)
        {
            if (session.AllEvents[i] is ErrorEvent err)
                return err;
        }
        return null;
    }

    public async Task StopAllAsync(CancellationToken ct = default)
    {
        List<IAgentSession> sessions;
        lock (_activeSessions)
            sessions = _activeSessions.ToList();

        var tasks = sessions
            .Where(s => s.State is SessionState.Running or SessionState.Starting)
            .Select(s => s.StopAsync(ct));

        await Task.WhenAll(tasks);
    }

    public IEventParser GetParser(string agentId)
    {
        if (!_parsers.TryGetValue(agentId, out var parser))
            throw new ArgumentException($"No event parser registered for agent '{agentId}'", nameof(agentId));
        parser.Reset();
        return parser;
    }
}

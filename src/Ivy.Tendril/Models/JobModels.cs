using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Subjects;
using System.Text.Json.Serialization;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Claude;
using Ivy.Tendril.Agents.Runtime;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Models;

public enum JobStatus
{
    Pending,
    Queued,
    Running,
    Completed,
    Failed,
    Timeout,
    Stopped,
    Blocked
}

public record JobItem
{
    /// <summary>
    /// Maximum number of output lines retained per job during execution.
    /// Lines beyond this limit are discarded (not just hidden from display).
    /// Memory is freed when EvictStaleJobs() removes completed jobs after 1 hour.
    /// Output is not persisted to SQLite—this in-memory queue is the only retention.
    /// </summary>
    private const int MaxOutputLines = 10_000;
    private int _completionGuard;
    private readonly Subject<string> _outputSubject = new();
    private StreamWriter? _rawLogWriter;
    private readonly object _rawLogLock = new();

    [JsonIgnore]
    public IObservable<string> OutputObservable => _outputSubject;

    public bool TryClaimCompletion() =>
        Interlocked.CompareExchange(ref _completionGuard, 1, 0) == 0;

    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public string PlanFile { get; set; } = "";
    public string Project { get; init; } = "";
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public JobArgsBase? TypedArgs { get; init; }
    public bool CancellationRequested { get; set; }
    public string? SessionId { get; set; }
    public string Provider { get; init; } = "claude";
    public string? Model { get; set; }
    public int Priority { get; init; }
    public List<string>? WaitForJobIds { get; init; }
    public decimal? Cost { get; set; }
    public int? Tokens { get; set; }

    // Soft-cleared from the Jobs app; still shown in plan Details history
    public bool Cleared { get; set; }

    [JsonIgnore]
    public IEventParser? EventParser { get; set; }
    private IEventSerializer? _eventSerializer;

    // Process handle for non-interactive execution
    public Process? Process { get; set; }
    public int? ProcessId { get; set; }
    public string? StatusMessage { get; set; }
    public ConcurrentQueue<string> OutputLines { get; set; } = new();
    public DateTime? LastOutputAt { get; set; }
    public CancellationTokenSource? TimeoutCts { get; set; }
    private volatile bool _staleOutputDetected;
    public bool StaleOutputDetected
    {
        get => _staleOutputDetected;
        set => _staleOutputDetected = value;
    }

    // Path to the .processing inbox file for CreatePlan job recovery
    public string? InboxFile { get; set; }

    // Pre-allocated plan ID for CreatePlan jobs (used for filesystem verification)
    public string? AllocatedPlanId { get; set; }

    // Reported by the agent via HTTP during execution
    public string? ReportedPlanId { get; set; }
    public string? ReportedPlanTitle { get; set; }

    // Agent launch details (persisted)
    public string? WorkingDirectory { get; set; }
    public string? CliCommand { get; set; }

    // Automatic logging metadata (transient, not persisted)
    [JsonIgnore] public string? CompiledPrompt { get; set; }
    [JsonIgnore] public int? ExitCode { get; set; }

    private string? _logFilePath;
    [JsonIgnore]
    public string? LogFilePath
    {
        get => _logFilePath;
        set
        {
            _logFilePath = value;
            OpenRawLogWriter();
        }
    }

    public void EnqueueOutput(string line)
    {
        EventParser ??= new ClaudeEventParser();
        _eventSerializer ??= new JsonEventSerializer();
        AppendToRawLog(line);
        foreach (var evt in EventParser.ParseLine(line))
        {
            if (evt is SystemEvent or UnknownEvent) continue;

            if (evt is SessionInitEvent { Model: not null } initEvt)
                Model = initEvt.Model;

            var serialized = _eventSerializer.Serialize(evt);
            OutputLines.Enqueue(serialized);
            while (OutputLines.Count > MaxOutputLines)
                OutputLines.TryDequeue(out _);
            _outputSubject.OnNext(serialized);
        }
    }

    public void EnqueueSystemOutput(string message)
    {
        _eventSerializer ??= new JsonEventSerializer();
        AppendToRawLog(message);
        var evt = new TextEvent { Kind = AgentEventKind.Text, Text = message };
        var serialized = _eventSerializer.Serialize(evt);
        OutputLines.Enqueue(serialized);
        while (OutputLines.Count > MaxOutputLines)
            OutputLines.TryDequeue(out _);
        _outputSubject.OnNext(serialized);
    }

    public void FlushParser()
    {
        if (EventParser is null) return;
        _eventSerializer ??= new JsonEventSerializer();
        foreach (var evt in EventParser.Flush())
        {
            var serialized = _eventSerializer.Serialize(evt);
            OutputLines.Enqueue(serialized);
            _outputSubject.OnNext(serialized);
        }
    }

    private void OpenRawLogWriter()
    {
        if (string.IsNullOrEmpty(_logFilePath)) return;
        var rawPath = Path.ChangeExtension(_logFilePath, ".raw.jsonl");
        try
        {
            _rawLogWriter = new StreamWriter(rawPath, append: true, System.Text.Encoding.UTF8)
            {
                AutoFlush = true
            };
        }
        catch
        {
            _rawLogWriter = null;
        }
    }

    private void AppendToRawLog(string line)
    {
        if (_rawLogWriter is null) return;
        lock (_rawLogLock)
        {
            try { _rawLogWriter.WriteLine(line); }
            catch { /* best-effort — don't crash the job */ }
        }
    }

    internal void CloseRawLog()
    {
        lock (_rawLogLock)
        {
            try { _rawLogWriter?.Dispose(); }
            catch { }
            _rawLogWriter = null;
        }
    }

    public void DisposeResources(ILogger? logger = null)
    {
        _outputSubject.OnCompleted();
        CloseRawLog();

        try
        {
            Process?.Dispose();
            logger?.LogDebug("Job {JobId}: Process disposed successfully", Id);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Job {JobId}: Failed to dispose Process", Id);
        }

        try
        {
            TimeoutCts?.Dispose();
            logger?.LogDebug("Job {JobId}: TimeoutCts disposed successfully", Id);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Job {JobId}: Failed to dispose TimeoutCts", Id);
        }

        Process = null;
        TimeoutCts = null;
    }
}

public record JobItemRow
{
    public string Id { get; init; } = "";
    /// <summary>
    /// Encoded animated-status string: "running:Running" while the job is running
    /// (so the Status column shimmers), "idle:Completed" / "idle:Failed" / etc.
    /// otherwise. Built via <see cref="Ivy.AnimatedStatusValue"/>.
    /// </summary>
    public string Status { get; init; } = "";
    public string PlanId { get; init; } = "";
    public string Plan { get; init; } = "";
    public string Type { get; init; } = "";
    public string Project { get; init; } = "";
    public string Timer { get; init; } = "";
    public string AgentOutput { get; init; } = "";
    public DateTime? LastOutputTimestamp { get; init; }
    public string Cost { get; init; } = "";
    public string Tokens { get; init; } = "";
    public string StatusMessage { get; init; } = "";
    public string? ErrorContext { get; init; }  // Multi-line error context for tooltip/expansion
}

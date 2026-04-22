using System.Collections.Concurrent;
using System.Diagnostics;

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
    public string[] Args { get; init; } = [];
    public bool CancellationRequested { get; set; }
    public string? SessionId { get; set; }
    public string Provider { get; init; } = "claude";
    public int Priority { get; init; }
    public decimal? Cost { get; set; }
    public int? Tokens { get; set; }

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

    public void EnqueueOutput(string line)
    {
        OutputLines.Enqueue(line);
        while (OutputLines.Count > MaxOutputLines)
            OutputLines.TryDequeue(out _);
    }

    public void DisposeResources()
    {
        try { Process?.Dispose(); } catch { }
        try { TimeoutCts?.Dispose(); } catch { }
        Process = null;
        TimeoutCts = null;
    }
}

public record JobItemRow
{
    public string Id { get; init; } = "";
    public JobStatus Status { get; init; }
    public string PlanId { get; init; } = "";
    public string Plan { get; init; } = "";
    public string Type { get; init; } = "";
    public string Project { get; init; } = "";
    public string Timer { get; init; } = "";
    public string LastOutput { get; init; } = "";
    public DateTime? LastOutputTimestamp { get; init; }
    public string Cost { get; init; } = "";
    public string Tokens { get; init; } = "";
    public string StatusMessage { get; init; } = "";
    public string? ErrorContext { get; init; }  // Multi-line error context for tooltip/expansion
}
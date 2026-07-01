namespace Ivy.Plugins.Hooks;

public record BeforeJobEvent
{
    public required string JobId { get; init; }
    public required string JobType { get; init; }
    public required string PlanFolder { get; init; }
    public required string Project { get; init; }
    public bool Cancelled { get; private set; }
    public string? CancellationReason { get; private set; }

    public void Cancel(string reason)
    {
        Cancelled = true;
        CancellationReason = reason;
    }
}

public enum JobStatus
{
    Completed,
    Failed,
    Stopped,
    TimedOut
}

public record AfterJobEvent
{
    public required string JobId { get; init; }
    public required string JobType { get; init; }
    public required JobStatus Status { get; init; }
    public required string PlanFolder { get; init; }
    public required string Project { get; init; }
    public int? ExitCode { get; init; }
    public TimeSpan Duration { get; init; }
}

public record BeforeCreatePlanEvent
{
    public required string Description { get; set; }
    public required string Project { get; set; }
    public string? SourceUrl { get; init; }
    public string? SourceIdentifier { get; init; }
    public bool Cancelled { get; private set; }
    public string? CancellationReason { get; private set; }

    public void Cancel(string reason)
    {
        Cancelled = true;
        CancellationReason = reason;
    }
}

public record AfterCreatePlanEvent
{
    public required string PlanFolder { get; init; }
    public required string PlanId { get; init; }
    public required string Project { get; init; }
}

public record ConfigSaveEvent
{
    public required object CurrentSettings { get; init; }
    public required object NewSettings { get; init; }
    public bool Rejected { get; private set; }
    public string? RejectionReason { get; private set; }

    public void Reject(string reason)
    {
        Rejected = true;
        RejectionReason = reason;
    }
}

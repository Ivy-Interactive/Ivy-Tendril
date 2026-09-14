namespace Ivy.Tendril.Models;

/// <summary>
///     What one inbox recovery pass did. Recovery used to be silent, which is why roughly 60
///     resurrections in the #2710 storm produced no explanatory log entries at all.
/// </summary>
/// <param name="Resurrected">Breadcrumbs whose owning job had crashed mid-flight and were renamed back to <c>.md</c>.</param>
/// <param name="Deleted">Breadcrumbs belonging to a job that already reached a terminal status.</param>
/// <param name="Orphaned">Breadcrumbs with no owning job, resurrected once.</param>
/// <param name="DeadLettered">Breadcrumbs past the recovery attempt cap, moved to <c>Inbox/DeadLetter</c>.</param>
/// <param name="BulkRefused">True when the pass would have resurrected more files than the bulk threshold allows, so it resurrected none.</param>
public record InboxRecoverySummary(
    int Resurrected,
    int Deleted,
    int Orphaned,
    int DeadLettered,
    bool BulkRefused)
{
    public static InboxRecoverySummary Empty { get; } = new(0, 0, 0, 0, false);

    /// <summary>True when the pass did nothing worth telling the operator about, which is the normal case.</summary>
    public bool IsEmpty => Resurrected == 0 && Deleted == 0 && Orphaned == 0 && DeadLettered == 0 && !BulkRefused;
}

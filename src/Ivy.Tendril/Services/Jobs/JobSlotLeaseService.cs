using System.Diagnostics;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Jobs;

/// <summary>
///     The machine wide half of the concurrency cap. <c>maxConcurrentJobs</c> used to be enforced only
///     by a per-process <see cref="SemaphoreSlim" />, so <c>30</c> in <c>config.yaml</c> became up to
///     120 concurrent agents across four instances sharing one TENDRIL_HOME. The OOM killer then
///     decided the real limit, which read from the outside as jobs hanging on their first tool call
///     (#2710). <c>tendril.db</c> is the only thing every instance already shares, so the lease lives
///     there and this wraps it with the policy: the TTL, this instance's identity, and the pid liveness
///     test.
///     <para>
///     Every call is best effort. The database is optional (the test constructor passes none) and a
///     <see cref="Microsoft.Data.Sqlite.SqliteException" /> must never fail a job submission, so a
///     failure degrades to the local semaphore alone and is logged once rather than propagated.
///     </para>
/// </summary>
internal sealed class JobSlotLeaseService
{
    /// <summary>
    ///     How long a lease survives without a heartbeat. Matches the <c>.master</c> heartbeat window,
    ///     and is three times the renewal period so two missed renewals are survivable.
    /// </summary>
    internal static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(90);

    /// <summary>
    ///     How much later than its lease a recorded process may have started. The agent process is
    ///     launched moments after its slot is taken, so anything beyond this is the OS having recycled
    ///     the pid onto something unrelated. Mirrors <c>JobService.PidReuseGrace</c>.
    /// </summary>
    private static readonly TimeSpan PidReuseGrace = TimeSpan.FromMinutes(5);

    private readonly IPlanDatabaseService? _database;
    private readonly ILogger _logger;
    private readonly int _ownerPid;
    private readonly string _machineName;

    /// <summary>Set once the first failure has been logged, so a broken database logs once, not per job.</summary>
    private int _degraded;

    internal JobSlotLeaseService(IPlanDatabaseService? database, ILogger logger, int? ownerPid = null,
        string? machineName = null)
    {
        _database = database;
        _logger = logger;
        _ownerPid = ownerPid ?? Environment.ProcessId;
        _machineName = machineName ?? Environment.MachineName;
    }

    /// <summary>Whether leases are actually being enforced, for tests and diagnostics.</summary>
    internal bool Enabled => _database != null;

    /// <summary>
    ///     Takes the machine wide slot for a job. Returns true when there is no database to ask: an
    ///     instance with no shared state cannot know the machine is full, and refusing every submission
    ///     would be far worse than falling back to the local bound.
    /// </summary>
    /// <param name="liveCount">Leases live machine wide, for the queued job's status message.</param>
    internal bool TryAcquire(string jobId, int maxConcurrentJobs, out int liveCount)
    {
        liveCount = 0;
        if (_database == null)
            return true;

        try
        {
            return _database.TryAcquireJobSlot(jobId, maxConcurrentJobs, _ownerPid, _machineName, LeaseTtl,
                IsPidAliveSince, out liveCount);
        }
        catch (Exception ex)
        {
            ReportDegraded(ex, "acquire");
            return true;
        }
    }

    internal void Release(string jobId)
    {
        if (_database == null)
            return;

        try
        {
            _database.ReleaseJobSlot(jobId);
        }
        catch (Exception ex)
        {
            ReportDegraded(ex, "release");
        }
    }

    /// <summary>
    ///     Records the agent pid on an existing lease, so an agent that outlives the instance that
    ///     launched it keeps the slot it is still consuming.
    /// </summary>
    internal void RecordAgentPid(string jobId, int agentPid)
    {
        if (_database == null)
            return;

        try
        {
            _database.SetJobSlotAgentPid(jobId, agentPid);
        }
        catch (Exception ex)
        {
            ReportDegraded(ex, "record agent pid on");
        }
    }

    /// <summary>Advances the heartbeat on the given leases, warning about any that had to be taken back.</summary>
    internal void Renew(IReadOnlyCollection<string> jobIds)
    {
        if (_database == null || jobIds.Count == 0)
            return;

        try
        {
            var reinserted = _database.RenewJobSlots(jobIds, _ownerPid, _machineName);
            if (reinserted.Count > 0)
                _logger.LogWarning(
                    "Re-took {Count} job slot lease(s) that had gone missing while still in use: {JobIds}. " +
                    "Another instance reclaimed them early, so the machine limit may be exceeded by that many until they finish",
                    reinserted.Count, string.Join(", ", reinserted));
        }
        catch (Exception ex)
        {
            ReportDegraded(ex, "renew");
        }
    }

    /// <summary>Drops leases nothing is using. Opportunistic: acquisition reclaims too.</summary>
    internal void ReclaimStale()
    {
        if (_database == null)
            return;

        try
        {
            _database.ReclaimStaleJobSlots(IsPidAliveSince, LeaseTtl, _machineName);
        }
        catch (Exception ex)
        {
            ReportDegraded(ex, "reclaim stale");
        }
    }

    /// <summary>Live leases machine wide, or 0 when there is no database to ask.</summary>
    internal int LiveCount()
    {
        if (_database == null)
            return 0;

        try
        {
            return _database.CountLiveJobSlots();
        }
        catch (Exception ex)
        {
            ReportDegraded(ex, "count");
            return 0;
        }
    }

    /// <summary>
    ///     Whether <paramref name="pid" /> is a live process that existed by the time the lease naming
    ///     it was taken. Without the start-time half, a recycled pid would keep a dead job's slot for
    ///     the life of the machine, which is the permanent-lockout failure mode this plan closes
    ///     elsewhere.
    /// </summary>
    private static bool IsPidAliveSince(int pid, DateTime acquiredAtUtc)
    {
        if (pid <= 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
                return false;

            // Unreadable StartTime means an elevated or system process, which is never one of ours.
            return process.StartTime.ToUniversalTime() <= acquiredAtUtc + PidReuseGrace;
        }
        catch
        {
            return false;
        }
    }

    private void ReportDegraded(Exception ex, string operation)
    {
        if (Interlocked.Exchange(ref _degraded, 1) != 0)
            return;

        _logger.LogWarning(ex,
            "Could not {Operation} a machine wide job slot lease. Falling back to the per-process concurrency " +
            "limit alone, which multiplies across instances. This is logged once per process",
            operation);
    }
}

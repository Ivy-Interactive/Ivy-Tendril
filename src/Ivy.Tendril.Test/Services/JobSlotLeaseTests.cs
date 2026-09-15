using System.Globalization;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

/// <summary>
///     The machine wide half of the concurrency cap (#2710). <c>maxConcurrentJobs</c> was enforced only
///     by a per-process semaphore, so <c>30</c> in <c>config.yaml</c> became up to 120 concurrent agents
///     across four instances sharing one TENDRIL_HOME and the OOM killer decided the real limit.
///     <para>
///     The TTL and reclaim cases go through <see cref="PlanDatabaseService" /> directly, because the
///     lease TTL is a policy constant on <see cref="JobSlotLeaseService" /> and an expiring heartbeat
///     otherwise means a 90 second test. The cases about <em>whose</em> lease it is go through
///     <see cref="JobSlotLeaseService" />, which owns the pid and machine identity.
///     </para>
/// </summary>
public class JobSlotLeaseTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-job-slots");
    private readonly PlanDatabaseService _db;
    private readonly string _dbPath;

    /// <summary>A pid no process on this machine can plausibly have, so liveness reads false.</summary>
    private const int DeadPid = 0x7FFFFFF0;

    public JobSlotLeaseTests()
    {
        _dbPath = Path.Combine(_tempDir.Path, $"tendril-slots-{Guid.NewGuid()}.db");
        _db = new PlanDatabaseService(_dbPath, NullLogger<PlanDatabaseService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    /// <summary>Liveness as the production predicate sees it, for the pids these tests care about.</summary>
    private static bool PidAliveSince(int pid, DateTime acquiredAtUtc) => pid == Environment.ProcessId;

    private static bool NothingIsAlive(int pid, DateTime acquiredAtUtc) => false;

    private bool TryAcquire(string jobId, int max, IPlanDatabaseService? db = null, int ownerPid = 4242,
        TimeSpan? ttl = null, Func<int, DateTime, bool>? isAlive = null) =>
        (db ?? _db).TryAcquireJobSlot(jobId, max, ownerPid, Environment.MachineName,
            ttl ?? TimeSpan.FromMinutes(10), isAlive ?? PidAliveSince, out _);

    [Fact]
    public void TryAcquireJobSlot_BlocksTheNPlusFirst_WhenNLeasesAreLive()
    {
        Assert.True(TryAcquire("job-1", 2));
        Assert.True(TryAcquire("job-2", 2));

        // The owning pid is alive as far as the predicate is concerned and the heartbeats are fresh, so
        // there is nothing to reclaim and no room to make.
        Assert.False(TryAcquire("job-3", 2, ownerPid: Environment.ProcessId));
        Assert.Equal(2, _db.CountLiveJobSlots());

        _db.ReleaseJobSlot("job-1");
        Assert.True(TryAcquire("job-3", 2, ownerPid: Environment.ProcessId));
        Assert.Equal(2, _db.CountLiveJobSlots());
    }

    [Fact]
    public void TryAcquireJobSlot_ReclaimsLease_WithDeadOwnerAndAgentPid()
    {
        // A lease left behind by an instance that was kill -9'd: both pids gone, heartbeat stale.
        Assert.True(TryAcquire("crashed", 1, ownerPid: DeadPid));
        _db.SetJobSlotAgentPid("crashed", DeadPid);

        Assert.True(TryAcquire("fresh", 1, ttl: TimeSpan.Zero, isAlive: NothingIsAlive));

        // Reclaimed rather than accumulated: the dead lease is gone, not merely outvoted.
        Assert.Equal(1, _db.CountLiveJobSlots());
    }

    [Fact]
    public void TryAcquireJobSlot_ReclaimsLease_WithExpiredHeartbeatAndNoLivePid()
    {
        Assert.True(TryAcquire("stale", 1, ownerPid: DeadPid));

        // No AgentPid at all: a job whose lease was taken but whose agent never launched.
        Assert.True(TryAcquire("fresh", 1, ttl: TimeSpan.Zero, isAlive: NothingIsAlive));
        Assert.Equal(1, _db.CountLiveJobSlots());
    }

    [Fact]
    public void TryAcquireJobSlot_DoesNotReclaimLease_WhenHeartbeatExpiredButAgentPidIsAlive()
    {
        // The owning instance is gone but left a detached agent behind. That agent is the resource being
        // capped, so its slot has to survive a heartbeat nobody is advancing any more.
        Assert.True(TryAcquire("detached", 1, ownerPid: DeadPid));
        _db.SetJobSlotAgentPid("detached", Environment.ProcessId);

        Assert.False(TryAcquire("fresh", 1, ttl: TimeSpan.Zero));
        Assert.Equal(1, _db.CountLiveJobSlots());
    }

    [Fact]
    public void TryAcquireJobSlot_ReclaimsLease_WhenOwningJobRowIsTerminal()
    {
        Assert.True(TryAcquire("finished", 1, ownerPid: Environment.ProcessId));
        _db.UpsertJob(new JobItem
        {
            Id = "finished",
            Type = Constants.JobTypes.ExecutePlan,
            PlanFile = "00601-Sample",
            Status = JobStatus.Completed,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = DateTime.UtcNow
        });

        // Defence in depth: a live pid and a fresh heartbeat, but the job it belongs to has finished, so
        // the lease is nobody's slot. A release that got lost must not cost a slot for the TTL.
        Assert.True(TryAcquire("next", 1, ownerPid: Environment.ProcessId));
        Assert.Equal(1, _db.CountLiveJobSlots());
    }

    [Fact]
    public void TryAcquireJobSlot_IsIdempotent_ForTheSameJobId()
    {
        Assert.True(TryAcquire("job-1", 1));

        // Re-acquisition by the holder is a heartbeat, not a second slot: a retry after a partial failure
        // must not double count itself out of the cap it already fits inside.
        Assert.True(TryAcquire("job-1", 1));
        Assert.Equal(1, _db.CountLiveJobSlots());
    }

    [Fact]
    public void TryAcquireJobSlot_ReportsLiveCount_ForTheQueuedJobsMessage()
    {
        _db.TryAcquireJobSlot("job-1", 2, 4242, Environment.MachineName, TimeSpan.FromMinutes(10),
            PidAliveSince, out var afterFirst);
        Assert.Equal(1, afterFirst);

        _db.TryAcquireJobSlot("job-2", 2, 4242, Environment.MachineName, TimeSpan.FromMinutes(10),
            PidAliveSince, out var afterSecond);
        Assert.Equal(2, afterSecond);

        var admitted = _db.TryAcquireJobSlot("job-3", 2, Environment.ProcessId, Environment.MachineName,
            TimeSpan.FromMinutes(10), PidAliveSince, out var whenRefused);
        Assert.False(admitted);
        Assert.Equal(2, whenRefused);
    }

    [Fact]
    public void TwoServiceInstances_OverOneDatabase_ShareOneCeiling()
    {
        // Two instances over one TENDRIL_HOME, which is the configuration that turned 30 into 120.
        using var second = new PlanDatabaseService(_dbPath, NullLogger<PlanDatabaseService>.Instance);

        var admitted = 0;
        for (var i = 0; i < 3; i++)
        {
            if (TryAcquire($"a-{i}", 3, ownerPid: 4242)) admitted++;
            if (TryAcquire($"b-{i}", 3, second, ownerPid: 5353)) admitted++;
        }

        // Six submissions across two instances, one cap of three. Per-process accounting would have
        // admitted all six.
        Assert.Equal(3, admitted);
        Assert.Equal(3, _db.CountLiveJobSlots());
        Assert.Equal(3, second.CountLiveJobSlots());
    }

    [Fact]
    public void RenewJobSlots_AdvancesHeartbeat_AndSetsAgentPid()
    {
        Assert.True(TryAcquire("job-1", 1));
        var before = ReadHeartbeat("job-1");
        _db.SetJobSlotAgentPid("job-1", Environment.ProcessId);

        Thread.Sleep(15);
        var reinserted = _db.RenewJobSlots(["job-1"], 4242, Environment.MachineName);

        Assert.Empty(reinserted);
        Assert.True(ReadHeartbeat("job-1") > before);
        // The agent pid survives the renewal: it is what keeps a detached agent's slot after the
        // instance that launched it exits.
        Assert.Equal(Environment.ProcessId, ReadAgentPid("job-1"));
    }

    [Fact]
    public void RenewJobSlots_ReTakesALeaseThatWentMissing_AndNamesIt()
    {
        Assert.True(TryAcquire("job-1", 2));
        _db.ReleaseJobSlot("job-1"); // As if another instance had reclaimed it early.

        var reinserted = _db.RenewJobSlots(["job-1"], 4242, Environment.MachineName);

        // Overshooting the cap by one until the job finishes beats letting healthy work run unaccounted.
        Assert.Equal(["job-1"], reinserted);
        Assert.Equal(1, _db.CountLiveJobSlots());
    }

    [Fact]
    public void ReclaimStaleJobSlots_LeavesForeignMachineRowsUntilTheirTtlExpires()
    {
        // A pid means nothing on another machine, so a foreign row goes on TTL alone, never on a
        // liveness answer about a local pid that happens to collide.
        _db.TryAcquireJobSlot("elsewhere", 5, 4242, "some-other-machine", TimeSpan.FromMinutes(10),
            PidAliveSince, out _);

        Assert.Equal(0, _db.ReclaimStaleJobSlots(NothingIsAlive, TimeSpan.FromMinutes(10), Environment.MachineName));
        Assert.Equal(1, _db.ReclaimStaleJobSlots(NothingIsAlive, TimeSpan.Zero, Environment.MachineName));
        Assert.Equal(0, _db.CountLiveJobSlots());
    }

    [Fact]
    public void JobSlotLeaseService_WithNoDatabase_AdmitsEverythingAndReportsDisabled()
    {
        // An instance with no shared state cannot know the machine is full, and refusing every
        // submission would be far worse than falling back to the local bound.
        var leases = new JobSlotLeaseService(null, NullLogger.Instance);

        Assert.False(leases.Enabled);
        Assert.True(leases.TryAcquire("job-1", 0, out var liveCount));
        Assert.Equal(0, liveCount);
        Assert.Equal(0, leases.LiveCount());

        // Every other entry point is a no-op rather than a throw.
        leases.RecordAgentPid("job-1", Environment.ProcessId);
        leases.Renew(["job-1"]);
        leases.ReclaimStale();
        leases.Release("job-1");
    }

    [Fact]
    public void JobSlotLeaseService_KeepsALeaseWhoseOwningInstanceIsThisProcess()
    {
        var leases = new JobSlotLeaseService(_db, NullLogger.Instance);

        Assert.True(leases.Enabled);
        Assert.True(leases.TryAcquire("job-1", 1, out var liveCount));
        Assert.Equal(1, liveCount);

        // This process is the owner and is demonstrably alive, so its own lease is never reclaimed out
        // from under it, not by ReclaimStale and not by the next acquisition.
        leases.ReclaimStale();
        Assert.Equal(1, leases.LiveCount());
        Assert.False(leases.TryAcquire("job-2", 1, out _));

        leases.Release("job-1");
        Assert.Equal(0, leases.LiveCount());
        Assert.True(leases.TryAcquire("job-2", 1, out _));
    }

    private DateTime ReadHeartbeat(string jobId) => ReadSlotColumn(jobId, "Heartbeat",
        s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private int ReadAgentPid(string jobId) => ReadSlotColumn(jobId, "AgentPid", int.Parse);

    private T ReadSlotColumn<T>(string jobId, string column, Func<string, T> parse)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWrite");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM JobSlots WHERE JobId = @id";
        cmd.Parameters.AddWithValue("@id", jobId);
        var value = cmd.ExecuteScalar();
        Assert.NotNull(value);
        return parse(value!.ToString()!);
    }
}

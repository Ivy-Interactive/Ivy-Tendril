using System.Diagnostics;
using System.Text.Json;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

/// <summary>
///     Guards the atomic claim in <see cref="MasterLock" />: the single place that decides who the master
///     is. Every test works against its own temp directory, and resets <see cref="MasterLock.Current" />
///     afterwards because that is process-wide state.
/// </summary>
[Collection("TendrilHome")]
public class MasterLockTests : IDisposable
{
    private readonly string _home;
    private readonly List<Process> _spawned = new();

    public MasterLockTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"tendril-masterlock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_home);
        MasterLock.Current = null;
    }

    public void Dispose()
    {
        MasterLock.Current = null;

        foreach (var process in _spawned)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch
            {
                // Best effort cleanup
            }

            process.Dispose();
        }

        try
        {
            if (Directory.Exists(_home))
                Directory.Delete(_home, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private string MasterFile => Path.Combine(_home, ".master");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private void WriteClaim(int pid, int port = 5010, string scheme = "http", TimeSpan? heartbeatAge = null)
    {
        var now = DateTime.UtcNow;
        var data = new MasterElectionService.MasterFileData
        {
            Pid = pid,
            Port = port,
            Scheme = scheme,
            StartedAt = now - (heartbeatAge ?? TimeSpan.Zero),
            Heartbeat = now - (heartbeatAge ?? TimeSpan.Zero)
        };
        File.WriteAllText(MasterFile, JsonSerializer.Serialize(data, JsonOptions));
    }

    /// <summary>A PID that is certain not to be running. 0 and negatives are rejected outright.</summary>
    private static int DeadPid()
    {
        for (var candidate = 999_999; candidate > 1000; candidate--)
            if (!MasterLock.IsProcessAlive(candidate))
                return candidate;

        throw new InvalidOperationException("Could not find a dead PID to test with.");
    }

    /// <summary>
    ///     A real, live process that is not this one, so its PID passes the liveness test. Using this
    ///     process's own PID would instead exercise the self-claim path.
    /// </summary>
    private Process StartIdleProcess()
    {
        // Loopback ping on Windows purely because it idles without needing a console; sleep elsewhere.
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1")
            : new ProcessStartInfo("/bin/sh", "-c \"sleep 60\"");
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start a helper process.");
        _spawned.Add(process);
        return process;
    }

    [Fact]
    public void TryAcquire_WritesAClaimWithPortZero()
    {
        var handle = MasterLock.TryAcquire(_home);

        Assert.NotNull(handle);
        Assert.Equal(Environment.ProcessId, handle!.Pid);
        Assert.Equal(0, handle.Port);
        Assert.True(File.Exists(MasterFile));

        var onDisk = MasterLock.Read(MasterFile);
        Assert.NotNull(onDisk);
        Assert.Equal(Environment.ProcessId, onDisk!.Pid);
        Assert.Equal(0, onDisk.Port);
    }

    [Fact]
    public void TryAcquire_SetsCurrent()
    {
        var handle = MasterLock.TryAcquire(_home);

        Assert.Same(handle, MasterLock.Current);
    }

    [Fact]
    public void TryAcquire_CreatesTheHomeDirectory()
    {
        var missing = Path.Combine(_home, "nested", "home");

        var handle = MasterLock.TryAcquire(missing);

        Assert.NotNull(handle);
        Assert.True(File.Exists(Path.Combine(missing, ".master")));
    }

    [Fact]
    public void TryAcquire_ReturnsNull_WhenAlreadyClaimed()
    {
        Assert.NotNull(MasterLock.TryAcquire(_home));

        // The claim exists, so the second attempt fails without truncating it. This is what makes two
        // launches milliseconds apart resolve to one master.
        Assert.Null(MasterLock.TryAcquire(_home));
    }

    [Fact]
    public void TryAcquire_ReturnsNull_WhenClaimedByADeadProcess()
    {
        WriteClaim(DeadPid());

        // Deliberately not "reclaims it": TryAcquire only claims, and the caller decides what to do
        // about an existing claim. Otherwise a racing launch could evict a healthy master.
        Assert.Null(MasterLock.TryAcquire(_home));
    }

    [Fact]
    public void TryAcquire_ReturnsNull_WhenHomeIsEmpty()
    {
        Assert.Null(MasterLock.TryAcquire(""));
        Assert.Null(MasterLock.Current);
    }

    [Fact]
    public void TryAcquire_OnlyOneOfManyRacingCallersWins()
    {
        var winners = 0;
        var threads = new List<Thread>();

        for (var i = 0; i < 16; i++)
            threads.Add(new Thread(() =>
            {
                if (MasterLock.TryAcquire(_home) != null)
                    Interlocked.Increment(ref winners);
            }));

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        Assert.Equal(1, winners);
    }

    [Fact]
    public void ReadLiveMaster_ReturnsNull_WhenNoFile()
    {
        Assert.Null(MasterLock.ReadLiveMaster(_home, out var reason));
        Assert.Equal("no .master file", reason);
    }

    [Fact]
    public void ReadLiveMaster_ReturnsNull_ForOurOwnClaim()
    {
        MasterLock.TryAcquire(_home);

        Assert.Null(MasterLock.ReadLiveMaster(_home, out var reason));
        Assert.Equal("claim belongs to this process", reason);
    }

    [Fact]
    public void ReadLiveMaster_ReturnsOurOwnClaim_WhenSelfIsNotExcluded()
    {
        MasterLock.TryAcquire(_home);

        // What server discovery needs: an embedded server talking to its own API is legitimate.
        var data = MasterLock.ReadLiveMaster(_home, out _, excludeSelf: false);

        Assert.NotNull(data);
        Assert.Equal(Environment.ProcessId, data!.Pid);
    }

    [Fact]
    public void ReadLiveMaster_ReturnsNull_ForADeadPid()
    {
        WriteClaim(DeadPid());

        Assert.Null(MasterLock.ReadLiveMaster(_home, out var reason));
        Assert.NotNull(reason);
        Assert.StartsWith("PID ", reason);
        Assert.EndsWith(" is dead", reason);
    }

    [Fact]
    public void ReadLiveMaster_ReturnsNull_ForAStaleHeartbeat()
    {
        // Our own PID, so the process really is alive: only the heartbeat disqualifies it.
        WriteClaim(Environment.ProcessId, heartbeatAge: MasterLock.StaleAfter + TimeSpan.FromSeconds(30));

        Assert.Null(MasterLock.ReadLiveMaster(_home, out var reason, excludeSelf: false));
        Assert.Equal("heartbeat is older than 90s", reason);
    }

    [Fact]
    public void ReadLiveMaster_ReturnsNull_ForAnUnreadableFile()
    {
        File.WriteAllText(MasterFile, "{ not json");

        Assert.Null(MasterLock.ReadLiveMaster(_home, out var reason));
        Assert.NotNull(reason);
        Assert.StartsWith("unreadable .master file", reason);
    }

    [Fact]
    public void ReadLiveMaster_ReturnsNull_WhenHomeIsEmpty()
    {
        Assert.Null(MasterLock.ReadLiveMaster("", out var reason));
        Assert.Equal("TENDRIL_HOME is not set", reason);
    }

    [Fact]
    public void TryReclaimStale_DeletesADeadClaimAndLetsTheNextAcquireWin()
    {
        WriteClaim(DeadPid());

        Assert.True(MasterLock.TryReclaimStale(_home));
        Assert.False(File.Exists(MasterFile));
        Assert.NotNull(MasterLock.TryAcquire(_home));
    }

    [Fact]
    public void TryReclaimStale_RefusesToDeleteAStaleClaimHeldByALiveProcess()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, heartbeatAge: MasterLock.StaleAfter + TimeSpan.FromSeconds(30));

        // The 2026-09-14 14:57 trigger: at load average 33-56 a starved 30s timer overshoots the 90s
        // window, and every CLI command then deleted the claim of a PID that was alive and listening.
        // A late heartbeat means "busy", not "dead", while the process is demonstrably running.
        Assert.False(MasterLock.TryReclaimStale(_home));
        Assert.True(File.Exists(MasterFile));
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void TryReclaimStale_RefusesToDeleteOurOwnStaleClaim()
    {
        WriteClaim(Environment.ProcessId, heartbeatAge: MasterLock.StaleAfter + TimeSpan.FromSeconds(30));

        Assert.False(MasterLock.TryReclaimStale(_home));
        Assert.True(File.Exists(MasterFile));
    }

    [Fact]
    public void TryReclaimStale_DeletesAStaleLiveClaimWhenForced()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, heartbeatAge: MasterLock.StaleAfter + TimeSpan.FromSeconds(30));

        // The launch guard's eviction, which is legitimate because it has already probed the holder and
        // found it silent: a genuinely hung master must not block every launch forever.
        Assert.True(MasterLock.TryReclaimStale(_home, null, ReclaimPolicy.ForceEvictLiveProcess));
        Assert.False(File.Exists(MasterFile));
    }

    [Fact]
    public void TryReclaimStale_StillDeletesADeadHoldersStaleClaim()
    {
        WriteClaim(DeadPid(), heartbeatAge: MasterLock.StaleAfter + TimeSpan.FromSeconds(30));

        // No live process to respect, so the policy is moot and the behaviour is unchanged.
        Assert.True(MasterLock.TryReclaimStale(_home));
        Assert.False(File.Exists(MasterFile));
    }

    [Fact]
    public void StaleAfter_IsThreeHeartbeatPeriods()
    {
        // Pinned so the threshold cannot drift away from the timer that is supposed to satisfy it.
        Assert.Equal(TimeSpan.FromSeconds(30), MasterLock.HeartbeatPeriod);
        Assert.Equal(3, MasterLock.MissedBeatsBeforeStale);
        Assert.Equal(MasterLock.HeartbeatPeriod * MasterLock.MissedBeatsBeforeStale, MasterLock.StaleAfter);
        Assert.Equal(TimeSpan.FromSeconds(90), MasterLock.StaleAfter);
    }

    [Fact]
    public void ForceRelease_DeletesALiveBeatingClaim()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id);

        // What 'tendril master release --force' is for: TryReclaimStale would refuse this claim at every
        // policy, because ReadLiveMaster accepts it.
        Assert.False(MasterLock.TryReclaimStale(_home, null, ReclaimPolicy.ForceEvictLiveProcess));
        Assert.True(MasterLock.ForceRelease(_home));
        Assert.False(File.Exists(MasterFile));
    }

    [Fact]
    public void ForceRelease_ReportsSuccess_WhenThereIsNoClaim()
    {
        Assert.True(MasterLock.ForceRelease(_home));
    }

    [Fact]
    public void TryReclaimStale_RefusesToDeleteOurOwnLiveClaim()
    {
        MasterLock.TryAcquire(_home);

        // The self-exclusion trap: a reclaim that treated "not another process" as "not alive" would
        // have this process quietly abandon its own mastership.
        Assert.False(MasterLock.TryReclaimStale(_home));
        Assert.True(File.Exists(MasterFile));
    }

    [Fact]
    public void TryReclaimStale_ReturnsTrue_WhenThereIsNothingToReclaim()
    {
        Assert.True(MasterLock.TryReclaimStale(_home));
    }

    [Fact]
    public void Publish_RecordsThePortAndScheme()
    {
        var handle = MasterLock.TryAcquire(_home)!;

        handle.Publish(5011, "https");

        Assert.Equal(5011, handle.Port);
        var onDisk = MasterLock.Read(MasterFile)!;
        Assert.Equal(5011, onDisk.Port);
        Assert.Equal("https", onDisk.Scheme);
    }

    [Fact]
    public void Publish_IsIgnored_WhenTheClaimWasTakenByAnotherProcess()
    {
        var handle = MasterLock.TryAcquire(_home)!;
        WriteClaim(DeadPid(), port: 1234);

        handle.Publish(5011, "http");

        // Our write must not clobber a claim that no longer names us.
        Assert.Equal(1234, MasterLock.Read(MasterFile)!.Port);
    }

    [Fact]
    public void Heartbeat_MovesTheHeartbeatForward()
    {
        var handle = MasterLock.TryAcquire(_home)!;
        var before = MasterLock.Read(MasterFile)!.Heartbeat;

        Thread.Sleep(20);
        handle.Heartbeat();

        Assert.True(MasterLock.Read(MasterFile)!.Heartbeat > before);
    }

    [Fact]
    public void StillOwned_IsFalse_AfterAnotherProcessTakesTheClaim()
    {
        var handle = MasterLock.TryAcquire(_home)!;
        Assert.True(handle.StillOwned());

        WriteClaim(DeadPid());

        // This is the demotion signal MasterElectionService's heartbeat looks for.
        Assert.False(handle.StillOwned());
    }

    [Fact]
    public void StillOwned_IsFalse_WhenTheClaimIsGone()
    {
        var handle = MasterLock.TryAcquire(_home)!;
        File.Delete(MasterFile);

        Assert.False(handle.StillOwned());
    }

    [Fact]
    public void Release_DeletesTheClaimAndClearsCurrent()
    {
        var handle = MasterLock.TryAcquire(_home)!;

        handle.Release();

        Assert.False(File.Exists(MasterFile));
        Assert.Null(MasterLock.Current);
    }

    [Fact]
    public void Release_LeavesAClaimThatBelongsToSomeoneElse()
    {
        var handle = MasterLock.TryAcquire(_home)!;
        var otherPid = DeadPid();
        WriteClaim(otherPid);

        handle.Release();

        Assert.True(File.Exists(MasterFile));
        Assert.Equal(otherPid, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Release_LeavesAnUnreadableClaimAlone()
    {
        var handle = MasterLock.TryAcquire(_home)!;
        File.WriteAllText(MasterFile, "{ not json");

        // Release requires positive ownership. A claim we cannot read is one we cannot prove is ours, and
        // deleting it is how a shutdown ends up leaving no master behind at all. Left in place it is still
        // recoverable: the next reader rejects it as unreadable and reclaims it with no live PID to respect.
        handle.Release();

        Assert.True(File.Exists(MasterFile));
    }

    [Fact]
    public void BeatOrReassert_ReturnsBeatReassertedAndDispossessed()
    {
        var handle = MasterLock.TryAcquire(_home)!;
        handle.Publish(5010, "https");

        Assert.Equal(MasterLockHandle.HeartbeatOutcome.Beat, handle.BeatOrReassert());

        File.Delete(MasterFile);
        Assert.Equal(MasterLockHandle.HeartbeatOutcome.Reasserted, handle.BeatOrReassert());
        var restored = MasterLock.Read(MasterFile)!;
        Assert.Equal(Environment.ProcessId, restored.Pid);
        Assert.Equal(5010, restored.Port);
        Assert.Equal("https", restored.Scheme);

        WriteClaim(DeadPid());
        Assert.Equal(MasterLockHandle.HeartbeatOutcome.Reasserted, handle.BeatOrReassert());
        Assert.Equal(Environment.ProcessId, MasterLock.Read(MasterFile)!.Pid);

        var alive = StartIdleProcess();
        WriteClaim(alive.Id);
        Assert.Equal(MasterLockHandle.HeartbeatOutcome.Dispossessed, handle.BeatOrReassert(out var foreignPid));
        Assert.Equal(alive.Id, foreignPid);

        // Dispossession writes nothing and deletes nothing: a live foreign master owns this claim.
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void BeatOrReassert_KeepsTheOriginalStartedAtAcrossAReassert()
    {
        var handle = MasterLock.TryAcquire(_home)!;
        var startedAt = MasterLock.Read(MasterFile)!.StartedAt;

        File.Delete(MasterFile);
        Assert.Equal(MasterLockHandle.HeartbeatOutcome.Reasserted, handle.BeatOrReassert());

        // Re-asserting is not a restart, and an uptime that resets on every file blip is a lie.
        Assert.Equal(startedAt, MasterLock.Read(MasterFile)!.StartedAt);
        Assert.Same(handle, MasterLock.Current);
    }

    [Fact]
    public void BeatOrReassert_ReassertsAnEmptyClaim()
    {
        var handle = MasterLock.TryAcquire(_home)!;
        File.WriteAllText(MasterFile, "");

        // The exact 2026-09-14 13:52Z state: a zero-byte .master, mid-write. It is a transient state of
        // our own file, not evidence that anyone took the claim.
        Assert.Equal(MasterLockHandle.HeartbeatOutcome.Reasserted, handle.BeatOrReassert());
        Assert.Equal(Environment.ProcessId, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Write_IsAtomicUnderConcurrentReaders()
    {
        MasterLock.TryAcquire(_home);
        var data = MasterLock.Read(MasterFile)!;

        var stop = false;
        var nulls = 0;
        var badPids = 0;
        var reads = 0;

        var reader = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                var seen = MasterLock.Read(MasterFile);
                reads++;
                if (seen == null) nulls++;
                else if (seen.Pid != Environment.ProcessId) badPids++;
            }
        });

        reader.Start();
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
        while (DateTime.UtcNow < deadline)
        {
            data.Heartbeat = DateTime.UtcNow;
            MasterLock.Write(MasterFile, data);
        }

        Volatile.Write(ref stop, true);
        reader.Join();

        // A truncate-in-place write publishes a zero-byte claim for the width of the write, and a reader
        // landing in that window reads "unreadable" - which is what made the CLI delete a live master's
        // claim. Every read must see either the old claim or the new one.
        Assert.True(reads > 0, "The reader thread never ran.");
        Assert.Equal(0, nulls);
        Assert.Equal(0, badPids);
    }

    [Fact]
    public void IsProcessAlive_IsTrueForUsAndFalseForNonsense()
    {
        Assert.True(MasterLock.IsProcessAlive(Environment.ProcessId));
        Assert.False(MasterLock.IsProcessAlive(0));
        Assert.False(MasterLock.IsProcessAlive(-1));
    }
}

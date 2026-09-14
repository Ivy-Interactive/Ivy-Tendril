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

    public MasterLockTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"tendril-masterlock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_home);
        MasterLock.Current = null;
    }

    public void Dispose()
    {
        MasterLock.Current = null;

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
    public void TryReclaimStale_DeletesAStaleHeartbeat()
    {
        WriteClaim(Environment.ProcessId, heartbeatAge: MasterLock.StaleAfter + TimeSpan.FromSeconds(30));

        Assert.True(MasterLock.TryReclaimStale(_home));
        Assert.False(File.Exists(MasterFile));
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
    public void IsProcessAlive_IsTrueForUsAndFalseForNonsense()
    {
        Assert.True(MasterLock.IsProcessAlive(Environment.ProcessId));
        Assert.False(MasterLock.IsProcessAlive(0));
        Assert.False(MasterLock.IsProcessAlive(-1));
    }
}

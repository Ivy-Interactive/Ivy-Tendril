using System.Diagnostics;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

/// <summary>
///     Guards the election's side of the claim: that it adopts a lock taken before anything was bound,
///     publishes the port it ended up on, notices when the claim stops naming it, and releases on the way
///     out.
/// </summary>
[Collection("TendrilHome")]
public class MasterElectionServiceTests : IDisposable
{
    private readonly string _home;
    private readonly string? _originalNotMaster;
    private readonly List<Process> _spawned = new();

    public MasterElectionServiceTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"tendril-election-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_home);

        _originalNotMaster = Environment.GetEnvironmentVariable("TENDRIL_NOT_MASTER");
        Environment.SetEnvironmentVariable("TENDRIL_NOT_MASTER", null);
        MasterLock.Current = null;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_NOT_MASTER", _originalNotMaster);
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

    /// <summary>A real, live process that is not this one, so its PID passes the liveness test.</summary>
    private Process StartIdleProcess()
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1")
            : new ProcessStartInfo("/bin/sh", "-c \"sleep 60\"");
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start a helper process.");
        _spawned.Add(process);
        return process;
    }

    /// <summary>A PID that is certain not to be running.</summary>
    private static int DeadPid()
    {
        for (var candidate = 999_999; candidate > 1000; candidate--)
            if (!MasterLock.IsProcessAlive(candidate))
                return candidate;

        throw new InvalidOperationException("Could not find a dead PID to test with.");
    }

    private (MasterElectionService Election, FakeLifetime Lifetime) CreateElection(string scheme = "http", int port = 5010)
    {
        var config = new ConfigService(new TendrilSettings(), _home);
        var lifetime = new FakeLifetime();
        var server = new FakeServer($"{scheme}://localhost:{port}");
        var election = new MasterElectionService(config, lifetime, server,
            NullLogger<MasterElectionService>.Instance);
        return (election, lifetime);
    }

    [Fact]
    public void Start_ClaimsWhenNothingHoldsTheLock()
    {
        var (election, _) = CreateElection();

        election.Start();

        Assert.True(election.IsMaster);
        Assert.True(File.Exists(MasterFile));
        Assert.Equal(Environment.ProcessId, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Start_AdoptsTheClaimTakenByTheLaunchGuard()
    {
        // The normal server-launch order: ServerInstanceGuard claims before anything binds, and the
        // election must adopt that handle rather than race for the lock a second time.
        var handle = MasterLock.TryAcquire(_home)!;
        var (election, _) = CreateElection();

        election.Start();

        Assert.True(election.IsMaster);
        Assert.Same(handle, MasterLock.Current);
    }

    [Fact]
    public void Start_RaisesMasterStatusChanged()
    {
        var (election, _) = CreateElection();
        var transitions = new List<bool>();
        election.MasterStatusChanged += transitions.Add;

        election.Start();

        Assert.Equal(new[] { true }, transitions);
    }

    [Fact]
    public void Start_DoesNotClaim_WhenNotMasterIsRequested()
    {
        Environment.SetEnvironmentVariable("TENDRIL_NOT_MASTER", "1");
        var (election, _) = CreateElection();

        election.Start();

        Assert.False(election.IsMaster);
        Assert.False(File.Exists(MasterFile));
    }

    [Fact]
    public void Start_DoesNotBecomeMaster_WhenTheLockIsAlreadyHeld()
    {
        // Whoever holds it, alive or not: the election only claims, and never evicts. Deciding what to
        // do about an existing claim is the launch guard's job.
        MasterLock.TryAcquire(_home);
        MasterLock.Current = null;

        var (election, _) = CreateElection();
        election.Start();

        Assert.False(election.IsMaster);
    }

    [Fact]
    public void ApplicationStarted_PublishesTheBoundPort()
    {
        var (election, lifetime) = CreateElection(scheme: "https", port: 5099);
        election.Start();

        // Before binding, the claim exists with port 0, which is what tells a sibling launch to wait
        // rather than to start a second server.
        Assert.Equal(0, MasterLock.Read(MasterFile)!.Port);

        lifetime.TriggerStarted();

        var onDisk = MasterLock.Read(MasterFile)!;
        Assert.Equal(5099, onDisk.Port);
        Assert.Equal("https", onDisk.Scheme);
        Assert.True(election.IsMaster);

        election.Dispose();
    }

    [Fact]
    public void Heartbeat_ReassertsTheClaimWhenTheFileDisappears()
    {
        var (election, lifetime) = CreateElection(scheme: "https", port: 5099);
        election.Start();
        lifetime.TriggerStarted();

        var transitions = new List<bool>();
        election.MasterStatusChanged += transitions.Add;

        // The 2026-09-14 14:57 incident: something deleted the claim of a master that was alive, bound and
        // running 28 agents. Demoting over that stops the inbox watcher and job services and leaves nobody
        // in charge; the CLI stays broken until a human hand-writes the file. Self-healing is the fix.
        File.Delete(MasterFile);

        InvokeHeartbeat(election);

        Assert.True(File.Exists(MasterFile));
        var restored = MasterLock.Read(MasterFile)!;
        Assert.Equal(Environment.ProcessId, restored.Pid);
        Assert.Equal(5099, restored.Port);
        Assert.Equal("https", restored.Scheme);

        // Still the master, and no transition at all: background services are never stopped and restarted
        // over a file blip.
        Assert.True(election.IsMaster);
        Assert.Empty(transitions);

        election.Dispose();
    }

    [Fact]
    public void Heartbeat_DoesNotSilentlyReturnWhenTheFileDisappears()
    {
        var (election, lifetime) = CreateElection();
        election.Start();
        lifetime.TriggerStarted();

        File.Delete(MasterFile);
        var before = DateTime.UtcNow;
        InvokeHeartbeat(election);

        // The "does something" half of the requirement, independent of what it wrote: the claim on disk is
        // beating again rather than the heartbeat having quietly given up.
        var restored = MasterLock.Read(MasterFile)!;
        Assert.True(restored.Heartbeat >= before.AddSeconds(-1));
        Assert.True(DateTime.UtcNow - restored.Heartbeat < TimeSpan.FromSeconds(10));

        election.Dispose();
    }

    [Fact]
    public void Heartbeat_ReassertsWhenTheClaimIsEmpty()
    {
        var (election, lifetime) = CreateElection();
        election.Start();
        lifetime.TriggerStarted();

        // The exact 13:52Z state: the file was empty, not missing, before it was deleted. A truncating
        // write publishes a zero-byte claim mid-flight, and reading it as "someone else has the claim"
        // would demote a master over its own write.
        File.WriteAllText(MasterFile, "");

        InvokeHeartbeat(election);

        Assert.Equal(Environment.ProcessId, MasterLock.Read(MasterFile)!.Pid);
        Assert.True(election.IsMaster);

        election.Dispose();
    }

    [Fact]
    public void Heartbeat_ReclaimsAClaimNamingADeadProcess()
    {
        var (election, lifetime) = CreateElection();
        election.Start();
        lifetime.TriggerStarted();

        var transitions = new List<bool>();
        election.MasterStatusChanged += transitions.Add;

        MasterLock.Write(MasterFile, new MasterElectionService.MasterFileData
        {
            Pid = DeadPid(),
            Port = 5010,
            Scheme = "http",
            StartedAt = DateTime.UtcNow,
            Heartbeat = DateTime.UtcNow
        });

        InvokeHeartbeat(election);

        // Nobody is holding a claim whose PID is dead, so there is no dispossession to accept.
        Assert.Equal(Environment.ProcessId, MasterLock.Read(MasterFile)!.Pid);
        Assert.True(election.IsMaster);
        Assert.Empty(transitions);

        election.Dispose();
    }

    [Fact]
    public void Heartbeat_DemotesWhenTheClaimNamesADifferentLiveProcess()
    {
        var (election, lifetime) = CreateElection();
        election.Start();
        lifetime.TriggerStarted();

        var transitions = new List<bool>();
        election.MasterStatusChanged += transitions.Add;

        // The one case that is a real loss of mastership: another launch took the claim and is running.
        var alive = StartIdleProcess();
        MasterLock.Write(MasterFile, new MasterElectionService.MasterFileData
        {
            Pid = alive.Id,
            Port = 5010,
            Scheme = "http",
            StartedAt = DateTime.UtcNow,
            Heartbeat = DateTime.UtcNow
        });

        InvokeHeartbeat(election);

        Assert.False(election.IsMaster);
        Assert.Equal(new[] { false }, transitions);

        // The foreign claim is left exactly as it was: it belongs to a live master now.
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);

        election.Dispose();
    }

    [Fact]
    public void Dispose_AfterDemotion_LeavesTheForeignClaimInPlace()
    {
        var (election, lifetime) = CreateElection();
        election.Start();
        lifetime.TriggerStarted();

        var alive = StartIdleProcess();
        MasterLock.Write(MasterFile, new MasterElectionService.MasterFileData
        {
            Pid = alive.Id,
            Port = 5010,
            Scheme = "http",
            StartedAt = DateTime.UtcNow,
            Heartbeat = DateTime.UtcNow
        });

        InvokeHeartbeat(election);
        Assert.False(election.IsMaster);

        // Shutdown must never delete the claim it was dispossessed of: Cleanup only releases while
        // IsMaster, which the demotion cleared.
        election.Dispose();

        Assert.True(File.Exists(MasterFile));
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Heartbeat_MovesTheHeartbeatForwardWhileStillOwned()
    {
        var (election, lifetime) = CreateElection();
        election.Start();
        lifetime.TriggerStarted();

        var before = MasterLock.Read(MasterFile)!.Heartbeat;
        Thread.Sleep(20);
        InvokeHeartbeat(election);

        Assert.True(MasterLock.Read(MasterFile)!.Heartbeat > before);
        Assert.True(election.IsMaster);

        election.Dispose();
    }

    [Fact]
    public void ApplicationStopping_ReleasesTheClaim()
    {
        var (election, lifetime) = CreateElection();
        election.Start();
        lifetime.TriggerStarted();

        lifetime.TriggerStopping();

        Assert.False(File.Exists(MasterFile));
        Assert.False(election.IsMaster);
        Assert.Null(MasterLock.Current);
    }

    [Fact]
    public void Dispose_LeavesAClaimThatBelongsToSomeoneElse()
    {
        var (election, _) = CreateElection();
        election.Start();

        var foreignPid = Environment.ProcessId + 1;
        MasterLock.Write(MasterFile, new MasterElectionService.MasterFileData
        {
            Pid = foreignPid,
            Port = 5010,
            Scheme = "http",
            StartedAt = DateTime.UtcNow,
            Heartbeat = DateTime.UtcNow
        });

        election.Dispose();

        Assert.True(File.Exists(MasterFile));
        Assert.Equal(foreignPid, MasterLock.Read(MasterFile)!.Pid);
    }

    /// <summary>
    ///     Fires the heartbeat callback directly. Waiting for the 30s timer is not a test.
    /// </summary>
    private static void InvokeHeartbeat(MasterElectionService election)
    {
        var method = typeof(MasterElectionService).GetMethod("UpdateHeartbeat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        method!.Invoke(election, [null]);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopped = new();
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => TriggerStopping();

        public void TriggerStarted() => _started.Cancel();

        public void TriggerStopping() => _stopping.Cancel();
    }

    private sealed class FakeServer(string address) : IServer
    {
        public IFeatureCollection Features { get; } = BuildFeatures(address);

        public void Dispose()
        {
        }

        public Task StartAsync<TContext>(Microsoft.AspNetCore.Hosting.Server.IHttpApplication<TContext> application,
            CancellationToken cancellationToken) where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private static IFeatureCollection BuildFeatures(string address)
        {
            var features = new FeatureCollection();
            features.Set<IServerAddressesFeature>(new BoundAddresses(address));
            return features;
        }

        private sealed class BoundAddresses(string address) : IServerAddressesFeature
        {
            public ICollection<string> Addresses { get; } = new List<string> { address };
            public bool PreferHostingUrls { get; set; }
        }
    }
}

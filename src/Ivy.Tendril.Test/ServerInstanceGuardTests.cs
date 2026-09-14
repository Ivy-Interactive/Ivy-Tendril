using System.Diagnostics;
using System.Text.Json;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

/// <summary>
///     Guards the duplicate-instance decision. The health probe is injected and the timings are shrunk,
///     so nothing here binds a port or speaks HTTP: what is under test is the verdict, not the transport.
/// </summary>
/// <remarks>
///     The "another master is alive" cases need a claim whose PID really is running and is not this
///     process, which is what <see cref="StartIdleProcess" /> provides. Using this process's own PID
///     would instead exercise the self-claim path.
/// </remarks>
[Collection("TendrilHome")]
public class ServerInstanceGuardTests : IDisposable
{
    private static readonly ServerInstanceGuard.ProbeTiming FastTiming = new(
        HealthAttempts: 2,
        HealthAttemptDelay: TimeSpan.FromMilliseconds(10),
        UnboundClaimWait: TimeSpan.FromMilliseconds(300),
        UnboundClaimPollInterval: TimeSpan.FromMilliseconds(20));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _home;
    private readonly string? _originalNotMaster;
    private readonly List<Process> _spawned = new();

    public ServerInstanceGuardTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"tendril-guard-{Guid.NewGuid():N}");
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

    private void WriteClaim(int pid, int port, string scheme = "http")
    {
        var now = DateTime.UtcNow;
        var data = new MasterElectionService.MasterFileData
        {
            Pid = pid,
            Port = port,
            Scheme = scheme,
            StartedAt = now,
            Heartbeat = now
        };
        File.WriteAllText(MasterFile, JsonSerializer.Serialize(data, JsonOptions));
    }

    private static int DeadPid()
    {
        for (var candidate = 999_999; candidate > 1000; candidate--)
            if (!MasterLock.IsProcessAlive(candidate))
                return candidate;

        throw new InvalidOperationException("Could not find a dead PID to test with.");
    }

    private ServerLaunchDecision Evaluate(bool useDesktop, out string? masterUrl,
        Func<string, bool>? healthProbe = null, ServerInstanceGuard.ProbeTiming? timing = null)
        => ServerInstanceGuard.Evaluate(_home, useDesktop, out masterUrl,
            healthProbe ?? (_ => true), timing: timing ?? FastTiming);

    [Fact]
    public void Proceeds_AndClaims_WhenNothingIsRunning()
    {
        var decision = Evaluate(useDesktop: false, out var masterUrl);

        Assert.Equal(ServerLaunchDecision.Proceed, decision);
        Assert.Null(masterUrl);
        Assert.NotNull(MasterLock.Current);
        Assert.True(File.Exists(MasterFile));
    }

    [Fact]
    public void Proceeds_WithoutClaiming_WhenNotMasterIsRequested()
    {
        Environment.SetEnvironmentVariable("TENDRIL_NOT_MASTER", "1");

        var decision = Evaluate(useDesktop: false, out _);

        // A deliberate secondary must keep working, and must not take the lock from the real master.
        Assert.Equal(ServerLaunchDecision.Proceed, decision);
        Assert.Null(MasterLock.Current);
        Assert.False(File.Exists(MasterFile));
    }

    [Fact]
    public void Proceeds_WhenHomeIsNotConfigured()
    {
        var decision = ServerInstanceGuard.Evaluate("", useDesktop: false, out _, _ => true, timing: FastTiming);

        // Onboarding runs before a home exists. No shared state means no duplicate to defend against.
        Assert.Equal(ServerLaunchDecision.Proceed, decision);
    }

    [Fact]
    public void Proceeds_WhenThisProcessAlreadyHoldsTheClaim()
    {
        Assert.Equal(ServerLaunchDecision.Proceed, Evaluate(useDesktop: false, out _));

        // The guard runs at both launch sites. Without the "already ours" short-circuit the second call
        // would refuse this very launch, because the claim it finds is one it cannot reclaim.
        Assert.Equal(ServerLaunchDecision.Proceed, Evaluate(useDesktop: false, out _));
        Assert.Equal(ServerLaunchDecision.Proceed, Evaluate(useDesktop: true, out _));
    }

    [Fact]
    public void Proceeds_WhenTheClaimBelongsToADeadProcess()
    {
        WriteClaim(DeadPid(), port: 5010);

        var decision = Evaluate(useDesktop: false, out _);

        Assert.Equal(ServerLaunchDecision.Proceed, decision);
        Assert.Equal(Environment.ProcessId, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Proceeds_WhenTheClaimIsStale()
    {
        var now = DateTime.UtcNow;
        var stale = now - MasterLock.StaleAfter - TimeSpan.FromMinutes(1);
        var alive = StartIdleProcess();
        File.WriteAllText(MasterFile, JsonSerializer.Serialize(new MasterElectionService.MasterFileData
        {
            Pid = alive.Id,
            Port = 5010,
            Scheme = "http",
            StartedAt = stale,
            Heartbeat = stale
        }, JsonOptions));

        // Alive but not beating and not answering: a hung master is reclaimed rather than deferred to
        // forever. The probe has to be explicit now - a claim whose PID is running is only evicted once it
        // has been proved silent, so leaving the probe at its default would defer to it instead.
        Assert.Equal(ServerLaunchDecision.Proceed, Evaluate(useDesktop: false, out _, _ => false));
        Assert.Equal(Environment.ProcessId, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Refuses_WhenAStaleClaimIsStillAnsweringItsHealthEndpoint()
    {
        var alive = StartIdleProcess();
        var stale = DateTime.UtcNow - MasterLock.StaleAfter - TimeSpan.FromMinutes(1);
        File.WriteAllText(MasterFile, JsonSerializer.Serialize(new MasterElectionService.MasterFileData
        {
            Pid = alive.Id,
            Port = 5016,
            Scheme = "http",
            StartedAt = stale,
            Heartbeat = stale
        }, JsonOptions));

        // The 2026-09-14 14:57 case exactly: at load average 33-56 the master's 30s timer overshot the 90s
        // window while it was still bound and answering. Evicting it there is what left 28 agents with no
        // master. Saturated is not hung, and the proof is that it answers.
        var decision = Evaluate(useDesktop: false, out var webUrl, _ => true);

        Assert.Equal(ServerLaunchDecision.Refuse, decision);
        Assert.Equal("http://localhost:5016", webUrl);

        // Untouched: the holder keeps its mastership, so its heartbeat can catch up.
        Assert.True(File.Exists(MasterFile));
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);

        // And the desktop launch has somewhere to send the user rather than starting a second server.
        Assert.Equal(ServerLaunchDecision.AttachToExisting, Evaluate(useDesktop: true, out var desktopUrl, _ => true));
        Assert.Equal("http://localhost:5016", desktopUrl);
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Refuses_WhenAHealthyMasterIsServingAndThisIsAWebLaunch()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5010);

        var decision = Evaluate(useDesktop: false, out var masterUrl);

        Assert.Equal(ServerLaunchDecision.Refuse, decision);
        Assert.Equal("http://localhost:5010", masterUrl);

        // The live claim survives: refusing must never disturb the master it deferred to.
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void AttachesToExisting_WhenAHealthyMasterIsServingAndThisIsADesktopLaunch()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5011, scheme: "https");

        var decision = Evaluate(useDesktop: true, out var masterUrl);

        // The desktop case has somewhere useful to send the user, which the web case does not.
        Assert.Equal(ServerLaunchDecision.AttachToExisting, decision);
        Assert.Equal("https://localhost:5011", masterUrl);
    }

    [Fact]
    public void ProbesTheHealthEndpointOfTheRecordedPort()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5012);

        var probed = new List<string>();
        Evaluate(useDesktop: false, out _, url =>
        {
            probed.Add(url);
            return true;
        });

        Assert.Equal(new[] { "http://localhost:5012/ivy/health" }, probed);
    }

    [Fact]
    public void RetriesTheHealthProbeBeforeGivingUp()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5013);

        var attempts = 0;
        var decision = Evaluate(useDesktop: false, out _, _ => ++attempts >= 2);

        // A busy master that misses one probe must not be evicted.
        Assert.Equal(ServerLaunchDecision.Refuse, decision);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Refuses_WhenALiveBeatingClaimDoesNotAnswerItsHealthEndpoint()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5014);

        var decision = Evaluate(useDesktop: false, out _, _ => false);

        // Only the master writes heartbeats, so a fresh one means a Tendril really is holding this
        // claim. Silence on the health endpoint is not enough to evict it: deferring costs the user one
        // launch, evicting costs them two masters running jobs at once. The stale-heartbeat window is
        // what frees a claim whose holder is gone.
        Assert.Equal(ServerLaunchDecision.Refuse, decision);
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Proceeds_WhenAnUnresponsiveClaimAlsoStoppedBeating()
    {
        var alive = StartIdleProcess();
        var stale = DateTime.UtcNow - MasterLock.StaleAfter - TimeSpan.FromMinutes(1);
        File.WriteAllText(MasterFile, JsonSerializer.Serialize(new MasterElectionService.MasterFileData
        {
            Pid = alive.Id,
            Port = 5014,
            Scheme = "http",
            StartedAt = stale,
            Heartbeat = stale
        }, JsonOptions));

        // The recycled-PID case: something else holds that id, so nothing is updating the heartbeat.
        var decision = Evaluate(useDesktop: false, out _, _ => false);

        Assert.Equal(ServerLaunchDecision.Proceed, decision);
        Assert.Equal(Environment.ProcessId, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Refuses_WhenALiveClaimNeverBindsAPort()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 0);

        var decision = Evaluate(useDesktop: false, out var masterUrl);

        // port 0 means "claimed, not yet bound". The holder is alive, so it is not ours to reclaim.
        Assert.Equal(ServerLaunchDecision.Refuse, decision);
        Assert.Null(masterUrl);
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public async Task WaitsForASiblingThatIsStillBinding()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 0);

        // The 15-40ms pairing from the crash log: the sibling wins the claim and publishes its port a
        // moment later. Waiting is the right answer, not declaring it dead.
        var publisher = Task.Run(async () =>
        {
            await Task.Delay(60);
            WriteClaim(alive.Id, port: 5015);
        });

        var timing = new ServerInstanceGuard.ProbeTiming(
            HealthAttempts: 1,
            HealthAttemptDelay: TimeSpan.FromMilliseconds(10),
            UnboundClaimWait: TimeSpan.FromSeconds(5),
            UnboundClaimPollInterval: TimeSpan.FromMilliseconds(20));

        var decision = Evaluate(useDesktop: true, out var masterUrl, timing: timing);
        await publisher;

        Assert.Equal(ServerLaunchDecision.AttachToExisting, decision);
        Assert.Equal("http://localhost:5015", masterUrl);
    }

    [Fact]
    public void Refuses_WhenTheClaimCannotBeTakenAtAll()
    {
        // A .master that cannot be created or read as a file stands in for the losing side of the
        // reclaim race: whatever the reason, a launch that cannot hold the lock must not become a
        // second server.
        Directory.CreateDirectory(MasterFile);

        var probeCalls = 0;
        var decision = ServerInstanceGuard.Evaluate(_home, useDesktop: false, out var masterUrl, _ =>
        {
            probeCalls++;
            return true;
        }, timing: FastTiming);

        Assert.Equal(ServerLaunchDecision.Refuse, decision);
        Assert.Null(masterUrl);
        Assert.Equal(0, probeCalls);
        Assert.Null(MasterLock.Current);
    }

    [Fact]
    public void DoesNotProbe_WhenThereIsNoClaimToProbe()
    {
        var probeCalls = 0;
        ServerInstanceGuard.Evaluate(_home, useDesktop: false, out _, _ =>
        {
            probeCalls++;
            return true;
        }, timing: FastTiming);

        Assert.Equal(0, probeCalls);
    }
}

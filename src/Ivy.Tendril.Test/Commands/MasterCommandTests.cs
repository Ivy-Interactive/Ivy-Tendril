using System.Diagnostics;
using System.Text.Json;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Commands;

/// <summary>
///     Guards the operator commands' decisions rather than their rendering: <see cref="MasterStatus.Describe" />,
///     <see cref="MasterStatus.ExitCode" /> and <see cref="MasterStatus.DecideRelease" /> are pure over the
///     claim plus an injected probe, so nothing here needs a server on the dev machine.
/// </summary>
[Collection("TendrilHome")]
public class MasterCommandTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _home;
    private readonly List<Process> _spawned = new();

    public MasterCommandTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"tendril-master-cmd-{Guid.NewGuid():N}");
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

    private static int DeadPid()
    {
        for (var candidate = 999_999; candidate > 1000; candidate--)
            if (!MasterLock.IsProcessAlive(candidate))
                return candidate;

        throw new InvalidOperationException("Could not find a dead PID to test with.");
    }

    private void WriteClaim(int pid, int port = 5010, string scheme = "http", TimeSpan? heartbeatAge = null)
    {
        var stamp = DateTime.UtcNow - (heartbeatAge ?? TimeSpan.Zero);
        File.WriteAllText(MasterFile, JsonSerializer.Serialize(new MasterElectionService.MasterFileData
        {
            Pid = pid,
            Port = port,
            Scheme = scheme,
            StartedAt = stamp,
            Heartbeat = stamp
        }, JsonOptions));
    }

    [Fact]
    public void Status_ReportsHealthy_WhenTheHolderIsAliveBeatingAndAnswering()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5010, scheme: "https");

        var report = MasterStatus.Describe(_home, _ => true);

        Assert.Equal(MasterVerdict.Healthy, report.Verdict);
        Assert.Equal(alive.Id, report.Pid);
        Assert.Equal(5010, report.Port);
        Assert.Equal("https", report.Scheme);
        Assert.True(report.ProcessAlive);
        Assert.True(report.HealthAnswered);
        Assert.Equal("https", report.SchemeThatAnswered);
        Assert.Equal(0, MasterStatus.ExitCode(report));
    }

    [Fact]
    public void Status_ProbesTheRecordedAddress()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5099, scheme: "https");

        var probed = new List<string>();
        MasterStatus.Describe(_home, url =>
        {
            probed.Add(url);
            return true;
        });

        Assert.Equal(new[] { "https://localhost:5099/ivy/health" }, probed);
    }

    [Fact]
    public void Status_ReportsSaturatedButServing_AndLeavesTheClaimAlone()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, heartbeatAge: MasterLock.StaleAfter + TimeSpan.FromMinutes(1));

        var report = MasterStatus.Describe(_home, _ => true);

        // The verdict the 2026-09-14 incident needed and nothing could say: late heartbeat, still serving.
        Assert.Equal(MasterVerdict.SaturatedButServing, report.Verdict);
        Assert.False(report.HeartbeatWithinThreshold);
        Assert.Contains("Do not break this claim", report.Explanation);

        // Exit 0: a script gating on this must not conclude the server is gone.
        Assert.Equal(0, MasterStatus.ExitCode(report));

        // Describing is not discovering: looking must not destroy the evidence.
        Assert.True(File.Exists(MasterFile));
        Assert.Equal(alive.Id, MasterLock.Read(MasterFile)!.Pid);
    }

    [Fact]
    public void Status_ReportsWedged_WhenTheHolderAnswersOnNeitherScheme()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5010);

        var report = MasterStatus.Describe(_home, _ => false);

        Assert.Equal(MasterVerdict.WedgedNotServing, report.Verdict);
        Assert.False(report.HealthAnswered);
        Assert.Equal(1, MasterStatus.ExitCode(report));
        Assert.True(File.Exists(MasterFile));
    }

    [Fact]
    public void Status_ReportsSchemeMismatch_WhenOnlyTheOtherSchemeAnswers()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5010, scheme: "https");

        // A hand-written recovery file with the wrong scheme: every command reports "failed to connect",
        // which says nothing about the claim being the problem.
        var report = MasterStatus.Describe(_home, url => url.StartsWith("http://", StringComparison.Ordinal));

        Assert.Equal(MasterVerdict.SchemeMismatch, report.Verdict);
        Assert.Equal("http", report.SchemeThatAnswered);
        Assert.Contains("records https", report.Explanation);
        Assert.Equal(1, MasterStatus.ExitCode(report));
    }

    [Fact]
    public void Status_ReportsDeadHolder()
    {
        WriteClaim(DeadPid());

        var report = MasterStatus.Describe(_home, _ => true);

        Assert.Equal(MasterVerdict.DeadHolder, report.Verdict);
        Assert.False(report.ProcessAlive);
        Assert.Equal(1, MasterStatus.ExitCode(report));
    }

    [Fact]
    public void Status_ReportsNoClaim_AndExitsNonZero()
    {
        var report = MasterStatus.Describe(_home, _ => true);

        Assert.Equal(MasterVerdict.NoClaim, report.Verdict);
        Assert.False(report.FileExists);
        Assert.Equal(1, MasterStatus.ExitCode(report));

        // The recovery instruction the incident had to be told by hand.
        Assert.Contains("re-asserts its own claim", report.Explanation);
    }

    [Fact]
    public void Status_ReportsUnreadable_WithoutDeletingTheFile()
    {
        File.WriteAllText(MasterFile, "");

        var report = MasterStatus.Describe(_home, _ => true);

        Assert.Equal(MasterVerdict.Unreadable, report.Verdict);
        Assert.True(report.FileExists);
        Assert.NotNull(report.ParseError);
        Assert.Equal(1, MasterStatus.ExitCode(report));
        Assert.True(File.Exists(MasterFile));
    }

    [Fact]
    public void Status_ReportsAClaimWithNoPortYet()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 0);

        var probed = 0;
        var report = MasterStatus.Describe(_home, _ =>
        {
            probed++;
            return true;
        });

        Assert.Equal(MasterVerdict.WedgedNotServing, report.Verdict);
        Assert.Equal(0, probed);
        Assert.Contains("retry in a moment", report.Explanation);
    }

    [Fact]
    public void Release_RefusesAMasterThatIsStillServing()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5010);

        var report = MasterStatus.Describe(_home, _ => true);
        var decision = MasterStatus.DecideRelease(report, force: false);

        // The resolution of the plan's release-live-master question: refuse unless forced. Deleting the
        // claim of a serving master is how you end up with two instances on one TENDRIL_HOME.
        Assert.Equal(MasterReleaseVerdict.RefuseServing, decision.Verdict);
        Assert.Contains("--force", decision.Message);
    }

    [Fact]
    public void Release_AllowsAServingMasterToBeBrokenWithForce()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5010);

        var report = MasterStatus.Describe(_home, _ => true);

        Assert.Equal(MasterReleaseVerdict.Release, MasterStatus.DecideRelease(report, force: true).Verdict);
    }

    [Fact]
    public void Release_DeletesAWedgedClaim()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5010);

        var report = MasterStatus.Describe(_home, _ => false);
        var decision = MasterStatus.DecideRelease(report, force: false);

        Assert.Equal(MasterReleaseVerdict.Release, decision.Verdict);
        Assert.Contains(alive.Id.ToString(), decision.Message);

        // What the operator ran by hand during the incident, as a command: the claim goes, and a live
        // process's mastership is only broken because it was proved not to be serving.
        Assert.True(MasterLock.ForceRelease(_home));
        Assert.False(File.Exists(MasterFile));
    }

    [Fact]
    public void Release_DeletesAnUnreadableClaim()
    {
        File.WriteAllText(MasterFile, "{ not json");

        var report = MasterStatus.Describe(_home, _ => true);
        var decision = MasterStatus.DecideRelease(report, force: false);

        Assert.Equal(MasterReleaseVerdict.Release, decision.Verdict);
        Assert.Contains("unreadable", decision.Message);
        Assert.True(MasterLock.ForceRelease(_home));
        Assert.False(File.Exists(MasterFile));
    }

    [Fact]
    public void Release_ReportsNothingToRelease_WhenThereIsNoClaim()
    {
        var report = MasterStatus.Describe(_home, _ => true);
        var decision = MasterStatus.DecideRelease(report, force: false);

        Assert.Equal(MasterReleaseVerdict.NothingToRelease, decision.Verdict);
    }

    [Fact]
    public void Status_SerialisesToJsonWithTheVerdictNamed()
    {
        var alive = StartIdleProcess();
        WriteClaim(alive.Id, port: 5010);

        var report = MasterStatus.Describe(_home, _ => true);
        var json = JsonSerializer.Serialize(report, MasterStatus.JsonOptions);

        // --json exists for scripts, so the verdict has to survive as something greppable rather than as
        // an enum ordinal that renumbers itself the next time a verdict is added.
        Assert.Contains("\"verdict\": \"Healthy\"", json);
        Assert.Contains($"\"pid\": {alive.Id}", json);
    }
}

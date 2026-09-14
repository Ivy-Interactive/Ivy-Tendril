using System.Text.Json;
using System.Text.Json.Serialization;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

/// <summary>What the recorded claim turned out to be.</summary>
public enum MasterVerdict
{
    /// <summary>Alive, beating, answering on the scheme it recorded.</summary>
    Healthy,

    /// <summary>Alive and answering, but its heartbeat is late: busy, not gone. Do not break it.</summary>
    SaturatedButServing,

    /// <summary>The recorded process is running but nothing answers on either scheme.</summary>
    WedgedNotServing,

    /// <summary>Only the other scheme answers, so the recorded one is what is failing every command.</summary>
    SchemeMismatch,

    /// <summary>The recorded PID is not running.</summary>
    DeadHolder,

    /// <summary>There is no claim at all, so no CLI command can reach a server.</summary>
    NoClaim,

    /// <summary>A claim exists but does not parse, e.g. the zero-byte file of an interrupted write.</summary>
    Unreadable
}

/// <summary>
///     Everything <c>tendril master status</c> knows, as data. <see cref="MasterStatus.Describe" /> is a
///     pure function over <c>.master</c> plus an injectable probe, so every verdict is unit-testable
///     without binding a port.
/// </summary>
public record MasterStatusReport(
    string Path, bool FileExists, string? ParseError,
    int Pid, int Port, string Scheme,
    DateTime StartedAt, DateTime Heartbeat,
    TimeSpan HeartbeatAge, bool HeartbeatWithinThreshold, TimeSpan Threshold,
    bool ProcessAlive, bool HealthAnswered, string? SchemeThatAnswered,
    MasterVerdict Verdict, string Explanation);

/// <summary>What <c>tendril master release</c> should do about a claim.</summary>
public enum MasterReleaseVerdict
{
    /// <summary>Nothing to do: there is no claim.</summary>
    NothingToRelease,

    /// <summary>The holder is alive and serving. Refuse unless the operator insists with --force.</summary>
    RefuseServing,

    /// <summary>Safe to break, subject to confirmation.</summary>
    Release
}

public record MasterReleaseDecision(MasterReleaseVerdict Verdict, string Message);

/// <summary>
///     Reads and explains the master claim without ever changing it.
/// </summary>
/// <remarks>
///     <see cref="Describe" /> never writes and never deletes, unlike <see cref="MasterClient.Discover" />.
///     That is the whole point of the command it backs: an operator diagnosing a broken claim has to be
///     able to look at it without the act of looking destroying the evidence.
/// </remarks>
public static class MasterStatus
{
    /// <summary>
    ///     How <c>--json</c> renders a report. The verdicts are written as names, not numbers, so a script
    ///     can test for "SaturatedButServing" without tracking the enum's ordering.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    ///     Describes the claim in <paramref name="tendrilHome" />, probing the recorded address (and, when
    ///     that is silent, the other scheme) to tell a saturated master from a wedged one and from a
    ///     hand-written file that names the wrong scheme.
    /// </summary>
    /// <param name="healthProbe">
    ///     Given a full <c>/ivy/health</c> URL, whether anything answered. Defaults to a real HTTP probe.
    /// </param>
    public static MasterStatusReport Describe(string tendrilHome, Func<string, bool>? healthProbe = null)
    {
        healthProbe ??= ServerInstanceGuard.DefaultHealthProbe;

        var path = string.IsNullOrEmpty(tendrilHome) ? ".master" : MasterLock.GetMasterFilePath(tendrilHome);
        var threshold = MasterLock.StaleAfter;

        if (string.IsNullOrEmpty(tendrilHome) || !File.Exists(path))
            return Empty(path, threshold, MasterVerdict.NoClaim,
                "No master claim. No CLI command can reach a server. A running master re-asserts its own claim " +
                $"within {MasterLock.HeartbeatPeriod.TotalSeconds:0}s (see 'Re-asserted the master claim' in crash.log); " +
                "if none appears, start one with 'tendril'.");

        var data = MasterLock.Read(path);
        if (data == null)
        {
            var unreadable = Empty(path, threshold, MasterVerdict.Unreadable,
                "The claim exists but does not parse, which is what an interrupted write leaves behind. " +
                "Nothing can discover the server until it is replaced: a running master re-asserts it on its " +
                "next beat, otherwise use 'tendril master release' and start one.");
            return unreadable with
            {
                FileExists = true,
                ParseError = "the .master file does not contain a readable claim"
            };
        }

        var age = DateTime.UtcNow - data.Heartbeat;
        var withinThreshold = age <= threshold;
        var alive = MasterLock.IsProcessAlive(data.Pid);
        var scheme = string.IsNullOrEmpty(data.Scheme) ? "http" : data.Scheme;

        var report = new MasterStatusReport(
            path, FileExists: true, ParseError: null,
            data.Pid, data.Port, scheme,
            data.StartedAt, data.Heartbeat,
            age, withinThreshold, threshold,
            alive, HealthAnswered: false, SchemeThatAnswered: null,
            MasterVerdict.DeadHolder, Explanation: "");

        if (!alive)
            return report with
            {
                Verdict = MasterVerdict.DeadHolder,
                Explanation = $"PID {data.Pid} is not running, so this claim is abandoned. The next launch reclaims " +
                              "it automatically; 'tendril master release' removes it now."
            };

        if (data.Port == 0)
            return report with
            {
                Verdict = MasterVerdict.WedgedNotServing,
                Explanation = $"PID {data.Pid} holds the claim but has not published a port yet, so there is nothing " +
                              "to connect to. If it is starting up, retry in a moment."
            };

        if (healthProbe($"{scheme}://localhost:{data.Port}/ivy/health"))
            return report with
            {
                HealthAnswered = true,
                SchemeThatAnswered = scheme,
                Verdict = withinThreshold ? MasterVerdict.Healthy : MasterVerdict.SaturatedButServing,
                Explanation = withinThreshold
                    ? $"Healthy. PID {data.Pid} is running, beating and answering on {scheme}."
                    : $"Saturated but serving. PID {data.Pid} is running and answering, its heartbeat is just late. " +
                      "Do not break this claim. If the CLI is failing, the server is busy, not gone."
            };

        // A hand-written recovery file with the wrong scheme produces the misleading "Failed to connect"
        // rather than anything about the file, so name both schemes explicitly.
        var otherScheme = scheme == "https" ? "http" : "https";
        if (healthProbe($"{otherScheme}://localhost:{data.Port}/ivy/health"))
            return report with
            {
                HealthAnswered = true,
                SchemeThatAnswered = otherScheme,
                Verdict = MasterVerdict.SchemeMismatch,
                Explanation = $"Scheme mismatch. The claim records {scheme}, but PID {data.Pid} only answers on " +
                              $"{otherScheme}://localhost:{data.Port}. Every CLI command will fail to connect until " +
                              $"the claim says {otherScheme}: 'tendril master release' and restart the server."
            };

        return report with
        {
            Verdict = MasterVerdict.WedgedNotServing,
            Explanation = $"Wedged. PID {data.Pid} is running but answers on neither {scheme} nor {otherScheme} at " +
                          $"port {data.Port}. Stop that process, or break the claim with 'tendril master release'."
        };
    }

    /// <summary>
    ///     0 when the recorded holder is alive and answering on the scheme it recorded, 1 otherwise, so a
    ///     script can gate on <c>tendril master status</c> without parsing anything.
    /// </summary>
    public static int ExitCode(MasterStatusReport report)
        => report.Verdict is MasterVerdict.Healthy or MasterVerdict.SaturatedButServing ? 0 : 1;

    /// <summary>
    ///     Whether the claim in <paramref name="report" /> may be broken. A master that is alive and
    ///     answering is refused unless <paramref name="force" /> is set: the operator who has decided a
    ///     serving master must go still gets their escape hatch, but not by accident at 3am.
    /// </summary>
    public static MasterReleaseDecision DecideRelease(MasterStatusReport report, bool force)
    {
        if (report.Verdict == MasterVerdict.NoClaim)
            return new MasterReleaseDecision(MasterReleaseVerdict.NothingToRelease, "No master claim to release.");

        if (report.ProcessAlive && report.HealthAnswered && !force)
            return new MasterReleaseDecision(MasterReleaseVerdict.RefuseServing,
                $"PID {report.Pid} is alive and answering on " +
                $"{report.SchemeThatAnswered}://localhost:{report.Port}, so it is still serving. Stop that process " +
                "first, or pass --force if you are certain the claim must go.");

        return new MasterReleaseDecision(MasterReleaseVerdict.Release,
            report.FileExists && report.ParseError == null
                ? $"Release the master claim held by PID {report.Pid}?"
                : "Release the unreadable master claim?");
    }

    private static MasterStatusReport Empty(string path, TimeSpan threshold, MasterVerdict verdict, string explanation)
        => new(path, FileExists: false, ParseError: null,
            Pid: 0, Port: 0, Scheme: "http",
            StartedAt: default, Heartbeat: default,
            HeartbeatAge: TimeSpan.Zero, HeartbeatWithinThreshold: false, Threshold: threshold,
            ProcessAlive: false, HealthAnswered: false, SchemeThatAnswered: null,
            verdict, explanation);
}

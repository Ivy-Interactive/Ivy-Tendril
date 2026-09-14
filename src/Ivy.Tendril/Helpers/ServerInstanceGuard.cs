using Ivy.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Helpers;

internal enum ServerLaunchDecision
{
    /// <summary>No other instance holds the lock: bind and come up as the master.</summary>
    Proceed,

    /// <summary>A healthy master is already serving: hand the user its URL instead of a second server.</summary>
    AttachToExisting,

    /// <summary>A healthy master is already serving and there is nothing useful to attach to.</summary>
    Refuse
}

/// <summary>
///     Decides whether a server launch may proceed, expressed as a pure function over
///     <c>TENDRIL_HOME/.master</c> plus an injectable health probe, so the decision is testable without
///     binding a port or speaking HTTP.
/// </summary>
/// <remarks>
///     This replaces the old "is the port in use" heuristic as the duplicate-instance defence. That
///     check only ever fired when <c>FindAvailablePort</c> was false, which the desktop launch path
///     sets to true unconditionally two lines above it, so every desktop launch walked straight past
///     it and came up as a second complete server against the same <c>TENDRIL_HOME</c>. Nothing here
///     consults <c>FindAvailablePort</c>.
/// </remarks>
internal static class ServerInstanceGuard
{
    /// <summary>
    ///     How patiently a live claim is probed before it is judged dead. Deliberately generous: a busy
    ///     master must never be evicted just because it was slow to answer.
    /// </summary>
    internal sealed record ProbeTiming(
        int HealthAttempts,
        TimeSpan HealthAttemptDelay,
        TimeSpan UnboundClaimWait,
        TimeSpan UnboundClaimPollInterval)
    {
        public static readonly ProbeTiming Default = new(
            HealthAttempts: 5,
            HealthAttemptDelay: TimeSpan.FromMilliseconds(500),
            UnboundClaimWait: TimeSpan.FromSeconds(15),
            UnboundClaimPollInterval: TimeSpan.FromMilliseconds(250));
    }

    internal static ServerLaunchDecision Evaluate(
        string tendrilHome,
        bool useDesktop,
        out string? masterUrl,
        Func<string, bool>? healthProbe = null,
        ILogger? logger = null,
        ProbeTiming? timing = null)
    {
        masterUrl = null;
        timing ??= ProbeTiming.Default;
        healthProbe ??= DefaultHealthProbe;

        // A deliberate secondary instance (--not-master / --slave) exists precisely to serve a second
        // UI against a shared TENDRIL_HOME. It must keep working.
        if (Environment.GetEnvironmentVariable("TENDRIL_NOT_MASTER") == "1")
        {
            Log(logger, "Proceed: TENDRIL_NOT_MASTER=1, this instance is a deliberate secondary.");
            return ServerLaunchDecision.Proceed;
        }

        if (string.IsNullOrEmpty(tendrilHome))
        {
            Log(logger, "Proceed: TENDRIL_HOME is not configured, no lock to take.");
            return ServerLaunchDecision.Proceed;
        }

        // We already hold the claim, which happens because the guard runs at both launch sites. Without
        // this the second call would fail to acquire, find a live claim naming itself, correctly decline
        // to reclaim it, and refuse our own launch.
        if (MasterLock.Current != null)
        {
            Log(logger, $"Proceed: this process already holds the master lock (PID {MasterLock.Current.Pid}).");
            return ServerLaunchDecision.Proceed;
        }

        if (MasterLock.TryAcquire(tendrilHome, logger) != null)
        {
            Log(logger, $"Proceed: acquired the master lock (PID {Environment.ProcessId}).");
            return ServerLaunchDecision.Proceed;
        }

        var live = MasterLock.ReadLiveMaster(tendrilHome, out var rejectReason);
        if (live == null)
            return ReclaimAndRetry(tendrilHome, rejectReason, logger);

        // A sibling that claimed but has not bound yet: the 15-40ms pairing seen in crash.log. Wait
        // for its port rather than declaring it dead, since it is very much alive.
        if (live.Port == 0)
        {
            live = WaitForBoundPort(tendrilHome, timing, logger) ?? live;
            if (live.Port == 0)
            {
                Log(logger, $"Refuse: PID {live.Pid} claimed mastership {timing.UnboundClaimWait.TotalSeconds:0}s ago and has not bound a port yet.");
                return ServerLaunchDecision.Refuse;
            }
        }

        var scheme = string.IsNullOrEmpty(live.Scheme) ? "http" : live.Scheme;
        var url = $"{scheme}://localhost:{live.Port}";

        if (!ProbeHealth(url, healthProbe, timing, logger))
        {
            // Not "evict it": the reclaim below still only deletes a claim MasterLock judges abandoned,
            // so a master that is alive and beating but too busy to answer keeps its mastership and this
            // launch defers to it. Silence plus a stale heartbeat is what actually frees the lock.
            Log(logger, $"Live claim (PID {live.Pid}) did not answer {url}/ivy/health after {timing.HealthAttempts} attempts.");
            return ReclaimAndRetry(tendrilHome, "no response from /ivy/health", logger);
        }

        masterUrl = url;

        if (useDesktop)
        {
            Log(logger, $"AttachToExisting: healthy master (PID {live.Pid}) at {url}, opening it instead of starting a second server.");
            return ServerLaunchDecision.AttachToExisting;
        }

        Log(logger, $"Refuse: healthy master (PID {live.Pid}) at {url}.");
        return ServerLaunchDecision.Refuse;
    }

    private static ServerLaunchDecision ReclaimAndRetry(string tendrilHome, string? reason, ILogger? logger)
    {
        MasterLock.TryReclaimStale(tendrilHome, logger);

        if (MasterLock.TryAcquire(tendrilHome, logger) != null)
        {
            Log(logger, $"Proceed: reclaimed a stale master lock ({reason}).");
            return ServerLaunchDecision.Proceed;
        }

        // Another process took the reclaimed lock in between. It is brand new, so it is alive.
        Log(logger, $"Refuse: a sibling took the master lock while we were reclaiming it ({reason}).");
        return ServerLaunchDecision.Refuse;
    }

    private static Services.MasterElectionService.MasterFileData? WaitForBoundPort(
        string tendrilHome, ProbeTiming timing, ILogger? logger)
    {
        var deadline = DateTime.UtcNow + timing.UnboundClaimWait;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(timing.UnboundClaimPollInterval);

            var current = MasterLock.ReadLiveMaster(tendrilHome);
            if (current == null)
                return null;
            if (current.Port > 0)
            {
                Log(logger, $"Sibling PID {current.Pid} bound port {current.Port} while we waited.");
                return current;
            }
        }

        return null;
    }

    private static bool ProbeHealth(string baseUrl, Func<string, bool> healthProbe, ProbeTiming timing, ILogger? logger)
    {
        for (var attempt = 1; attempt <= timing.HealthAttempts; attempt++)
        {
            try
            {
                if (healthProbe($"{baseUrl}/ivy/health"))
                    return true;
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Health probe attempt {Attempt} against {Url} threw", attempt, baseUrl);
            }

            if (attempt < timing.HealthAttempts)
                Thread.Sleep(timing.HealthAttemptDelay);
        }

        return false;
    }

    /// <summary>
    ///     Any HTTP response counts as alive, 4xx and 5xx included: the question is whether a server is
    ///     listening, not whether it is happy. The certificate check is waived because a desktop
    ///     instance serves HTTPS with a self-signed cert.
    /// </summary>
    private static bool DefaultHealthProbe(string url)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            using var response = client.GetAsync(url).GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Log(ILogger? logger, string message)
    {
        logger?.LogInformation("ServerInstanceGuard: {Message}", message);
        CrashLog.Write($"[{DateTime.UtcNow:O}] ServerInstanceGuard (PID {Environment.ProcessId}): {message}");
    }
}

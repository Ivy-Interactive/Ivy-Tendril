using Ivy.Helpers;
using Ivy.Tendril.Helpers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

/// <summary>
///     Extends <see cref="IStartable" /> because the election is the one background service whose
///     start order matters: BackgroundServiceActivator has to run it, through this interface, before it
///     resolves anything that a non-master must not construct.
/// </summary>
public interface IMasterElectionService : IStartable, IDisposable
{
    bool IsMaster { get; }

    /// <summary>
    /// Raised on every transition of <see cref="IsMaster" />, so master-only background services can
    /// be stopped when this instance is demoted and started again if it is promoted.
    /// </summary>
    event Action<bool>? MasterStatusChanged;
}

public class MasterElectionService(
    IConfigService configService,
    IHostApplicationLifetime appLifetime,
    IServer server,
    ILogger<MasterElectionService> logger)
    : IMasterElectionService, IStartable
{
    private Timer? _heartbeatTimer;
    private MasterLockHandle? _handle;
    private bool _isMaster;

    /// <summary>
    ///     Set synchronously by <see cref="Start" />, before it returns, which is what lets
    ///     BackgroundServiceActivator elect first and then decide what to start.
    /// </summary>
    public bool IsMaster
    {
        get => _isMaster;
        private set
        {
            if (_isMaster == value) return;
            _isMaster = value;
            MasterStatusChanged?.Invoke(value);
        }
    }

    public event Action<bool>? MasterStatusChanged;

    public void Start()
    {
        if (Environment.GetEnvironmentVariable("TENDRIL_NOT_MASTER") == "1")
        {
            logger.LogInformation("Running in non-master mode (TENDRIL_NOT_MASTER=1)");
            return;
        }

        if (string.IsNullOrEmpty(configService.TendrilHome))
            return;

        // On the normal server-launch path the claim was already taken atomically by
        // ServerInstanceGuard before anything bound, so adopt it rather than racing for it again.
        // Tests, onboarding and embedded `tendril run` uses reach here with no handle and claim now.
        _handle = MasterLock.Current ?? MasterLock.TryAcquire(configService.TendrilHome, logger);

        if (_handle == null)
        {
            logger.LogWarning("Another Tendril master is running. This instance will not accept CLI commands.");
            return;
        }

        IsMaster = true;

        appLifetime.ApplicationStarted.Register(OnApplicationStarted);
        appLifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    private void OnApplicationStarted()
    {
        var bound = GetBoundAddress();
        if (bound == null)
        {
            logger.LogWarning("Could not determine bound port: .master file not written");
            IsMaster = false;
            return;
        }

        // The claim exists already with port 0; this is what turns it into one a sibling launch can
        // attach to instead of starting a second server.
        _handle?.Publish(bound.Value.Port, bound.Value.Scheme);
        _heartbeatTimer = new Timer(UpdateHeartbeat, null, MasterLock.HeartbeatPeriod, MasterLock.HeartbeatPeriod);
        logger.LogInformation("Master election won, listening on port {Port}", bound.Value.Port);
    }

    private void OnApplicationStopping()
    {
        Cleanup();
    }

    /// <summary>
    ///     The heartbeat is also the claim's repair pass. A claim that went missing is re-asserted rather
    ///     than given up on: this instance is the one with the port bound and the jobs running, and
    ///     demoting over a vanished file stops the inbox watcher and job services while leaving nobody at
    ///     all in charge. Only a claim now held by a different, live process is a real dispossession.
    /// </summary>
    private void UpdateHeartbeat(object? state)
    {
        if (_handle == null || !IsMaster) return;

        try
        {
            switch (_handle.BeatOrReassert(out var foreignPid))
            {
                case MasterLockHandle.HeartbeatOutcome.Beat:
                    break;

                case MasterLockHandle.HeartbeatOutcome.Reasserted:
                    // Warning, not Debug: a claim that vanished under a live master is the 14:57 incident.
                    LogClaimEvent($"Re-asserted the master claim (PID {_handle.Pid}, port {_handle.Port}): " +
                                  "the .master file was missing or no longer ours");
                    break;

                case MasterLockHandle.HeartbeatOutcome.Dispossessed:
                    LogClaimEvent($"Demoting: the .master file now names live PID {foreignPid}, not {_handle.Pid}");
                    Demote();
                    break;
            }
        }
        catch (Exception ex)
        {
            // A heartbeat that throws is not routine: it is the write that keeps the claim alive.
            logger.LogWarning(ex, "Heartbeat failed");
        }
    }

    /// <summary>
    ///     Stops acting as the master without touching the claim. Deliberately no re-promotion loop: a
    ///     live foreign master owns the claim now, and this instance's job is to stop behaving like one.
    ///     Clearing <see cref="IsMaster" /> is also what stops <see cref="Cleanup" /> from deleting the
    ///     claim this instance lost.
    /// </summary>
    private void Demote()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        IsMaster = false;
    }

    /// <summary>
    ///     Claim transitions go to <c>crash.log</c> as well as the logger: it is the sink that survives a
    ///     wedged server and the one an operator actually reads. The 14:57 incident wrote zero lines about
    ///     the claim it lost.
    /// </summary>
    private void LogClaimEvent(string message)
    {
        logger.LogWarning("MasterElection: {Message}", message);
        CrashLog.Write($"[{DateTime.UtcNow:O}] MasterElection (PID {Environment.ProcessId}): {message}");
    }

    private (int Port, string Scheme)? GetBoundAddress()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses == null || addresses.Count == 0) return null;

        var address = addresses.First();
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
            return (uri.Port, uri.Scheme);

        var lastColon = address.LastIndexOf(':');
        if (lastColon >= 0 && int.TryParse(address[(lastColon + 1)..], out var port))
            return (port, "http");

        return null;
    }

    private void Cleanup()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        if (IsMaster)
            _handle?.Release();

        _handle = null;
        IsMaster = false;
    }

    public void Dispose() => Cleanup();

    public record MasterFileData
    {
        public int Pid { get; set; }
        public int Port { get; set; }
        public string Scheme { get; set; } = "http";
        public DateTime StartedAt { get; set; }
        public DateTime Heartbeat { get; set; }
    }
}

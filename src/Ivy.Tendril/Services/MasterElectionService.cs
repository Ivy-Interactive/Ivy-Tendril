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
        _heartbeatTimer = new Timer(UpdateHeartbeat, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        logger.LogInformation("Master election won, listening on port {Port}", bound.Value.Port);
    }

    private void OnApplicationStopping()
    {
        Cleanup();
    }

    /// <summary>
    ///     The heartbeat is also the demotion check: if the claim no longer names this process, another
    ///     launch judged it stale and took it, and this instance must stop behaving like the master.
    /// </summary>
    private void UpdateHeartbeat(object? state)
    {
        if (_handle == null || !IsMaster) return;

        try
        {
            if (!_handle.StillOwned())
            {
                logger.LogWarning("Lost mastership: the .master file no longer names PID {Pid}", _handle.Pid);
                IsMaster = false;
                return;
            }

            _handle.Heartbeat();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to update heartbeat");
        }
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

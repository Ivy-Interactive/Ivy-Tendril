using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Tunnel;

public sealed class CloudflaredService : ICloudflaredService, IStartable, IDisposable
{
    private readonly IConfigService _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IServer _server;
    private readonly ILogger<CloudflaredService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _supervisorTask;
    private TunnelSession? _currentSession;
    private bool _isInstalled;
    private TunnelStatus _status = TunnelStatus.Disabled;

    public CloudflaredService(
        IConfigService config,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime appLifetime,
        IServer server,
        ILogger<CloudflaredService> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _appLifetime = appLifetime;
        _server = server;
        _logger = logger;
    }

    public string? TunnelUrl => _currentSession?.TunnelUrl;
    public TunnelStatus Status => _status;
    public bool IsConnected => _status == TunnelStatus.Connected;
    public bool IsInstalled => _isInstalled;

    public event Action<TunnelStatus>? StatusChanged;

    private void SetStatus(TunnelStatus status)
    {
        if (_status == status) return;
        _status = status;
        StatusChanged?.Invoke(status);
    }

    public void Start()
    {
        var tunnelConfig = _config.Settings.Tunnel;
        if (tunnelConfig is not { Enabled: true })
        {
            _logger.LogDebug("Tunnel is disabled, skipping");
            return;
        }

        StartSupervisor();
    }

    public async Task<bool> CheckInstalledAsync(CancellationToken ct = default)
    {
        var installer = new CloudflaredInstaller(
            _config.TendrilHome, _httpClientFactory, _logger);
        var existing = installer.FindExisting();
        _isInstalled = existing is not null;
        return _isInstalled;
    }

    public async Task InstallAsync(CancellationToken ct = default)
    {
        var installer = new CloudflaredInstaller(
            _config.TendrilHome, _httpClientFactory, _logger);
        await installer.DownloadAsync(ct);
        _isInstalled = true;
    }

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _config.Settings.Tunnel ??= new TunnelConfig();
        _config.Settings.Tunnel.Enabled = true;
        _config.SaveSettings();

        StartSupervisor();
    }

    public async Task DeactivateAsync()
    {
        _cts?.Cancel();
        _currentSession?.Stop();

        var supervisorTask = _supervisorTask;
        if (supervisorTask is not null)
        {
            // Never block the calling (UI dispatcher) thread: the supervisor's
            // own event callbacks marshal back to it, so a synchronous .Wait()
            // here deadlocks until it times out.
            try { await supervisorTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
        }

        _currentSession?.Dispose();
        _currentSession = null;
        _cts?.Dispose();
        _cts = null;
        _supervisorTask = null;

        _config.Settings.Tunnel ??= new TunnelConfig();
        _config.Settings.Tunnel.Enabled = false;
        _config.SaveSettings();

        SetStatus(TunnelStatus.Disabled);
    }

    private void StartSupervisor()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        SetStatus(TunnelStatus.Connecting);
        _supervisorTask = Task.Run(() => SupervisorLoopAsync(_cts.Token));
    }

    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HealthCheckInitialDelay = TimeSpan.FromSeconds(3);

    private async Task WaitForTunnelHealthyAsync(string tunnelUrl, CancellationToken ct)
    {
        using var http = _httpClientFactory.CreateClient("TunnelHealthCheck");
        http.Timeout = TimeSpan.FromSeconds(10);

        // Give cloudflare a moment to publish DNS for the freshly created hostname before
        // the first probe. Probing too early returns NXDOMAIN, which the OS resolver caches
        // negatively and then keeps returning for the negative-TTL window even after the
        // record goes live — making the tunnel look broken far longer than it actually is.
        try { await Task.Delay(HealthCheckInitialDelay, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }

        var deadline = DateTime.UtcNow + HealthCheckTimeout;
        _logger.LogInformation("Waiting for tunnel to become routable: {Url}", tunnelUrl);

        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                using var response = await http.GetAsync(tunnelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!IsTunnelNotReady(response.StatusCode))
                {
                    _logger.LogInformation("Tunnel is routable after {Attempts} attempt(s) (HTTP {Status})",
                        attempt, (int)response.StatusCode);
                    return;
                }

                _logger.LogDebug("Tunnel not ready yet (HTTP {Status}), attempt {Attempt}",
                    (int)response.StatusCode, attempt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Expected early on: DNS for the new hostname has not propagated yet, or the
                // edge connections are still being established. Keep polling.
                _logger.LogDebug("Tunnel health check attempt {Attempt} failed: {Error}", attempt, ex.Message);
            }

            if (DateTime.UtcNow >= deadline)
            {
                // Never report a tunnel as connected when it isn't actually routable. Throwing
                // lets the supervisor tear this session down and try a fresh one (new hostname,
                // no poisoned negative-DNS cache) instead of leaving a dead "Connected" tunnel.
                throw new TimeoutException(
                    $"Tunnel did not become routable within {HealthCheckTimeout.TotalSeconds:0}s ({tunnelUrl})");
            }

            await Task.Delay(HealthCheckInterval, ct);
        }
    }

    // Status codes cloudflare returns while a tunnel exists but is not yet routable end to end:
    // 502 Bad Gateway, 504 Gateway Timeout, and 530 (Argo/Cloudflare Tunnel error, e.g. 1033).
    // Any other completed response means the request reached our origin through the tunnel.
    private static bool IsTunnelNotReady(System.Net.HttpStatusCode status) =>
        status is System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.GatewayTimeout
            or (System.Net.HttpStatusCode)530;

    private async Task SupervisorLoopAsync(CancellationToken ct)
    {
        var tunnelConfig = _config.Settings.Tunnel!;
        var maxRestarts = tunnelConfig.MaxRestarts;

        string binaryPath;
        if (!string.IsNullOrEmpty(tunnelConfig.BinaryPath))
        {
            binaryPath = tunnelConfig.BinaryPath;
        }
        else
        {
            var installer = new CloudflaredInstaller(
                _config.TendrilHome, _httpClientFactory, _logger);
            binaryPath = await installer.EnsureInstalledAsync(ct);
        }

        if (!_appLifetime.ApplicationStarted.IsCancellationRequested)
        {
            _logger.LogInformation("Waiting for server before launching tunnel");
            await Task.Delay(Timeout.Infinite, _appLifetime.ApplicationStarted)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (ct.IsCancellationRequested) return;
        }

        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var originUrl = addresses?.FirstOrDefault()
            ?? $"http://localhost:{tunnelConfig.Port}";
        originUrl = originUrl.Replace("://localhost:", "://127.0.0.1:");
        _logger.LogInformation("Tunnel origin URL: {OriginUrl}", originUrl);

        var consecutiveFailures = 0;

        while (!ct.IsCancellationRequested && consecutiveFailures < maxRestarts)
        {
            TunnelSession? session = null;
            try
            {
                SetStatus(TunnelStatus.Connecting);
                session = new TunnelSession(binaryPath, originUrl, _logger);
                _currentSession = session;
                var url = await session.StartAsync(ct);
                await WaitForTunnelHealthyAsync(url, ct);
                consecutiveFailures = 0;
                SetStatus(TunnelStatus.Connected);

                await session.WaitForExitAsync(ct);
                if (ct.IsCancellationRequested) break;
                _logger.LogWarning("Tunnel process exited unexpectedly");
                SetStatus(TunnelStatus.Connecting);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                SetStatus(TunnelStatus.Connecting);
                _logger.LogWarning(ex, "Tunnel session failed (attempt {Count}/{Max})",
                    consecutiveFailures, maxRestarts);
            }
            finally
            {
                // Tear down only our own session. Guard the shared field so a
                // superseded supervisor can never dispose or null a newer
                // generation's session (which would kill a freshly started tunnel).
                session?.Dispose();
                if (ReferenceEquals(_currentSession, session))
                    _currentSession = null;
            }

            if (ct.IsCancellationRequested) break;

            var delay = TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, consecutiveFailures - 1), 60));
            _logger.LogInformation("Restarting tunnel in {Delay}s", delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }

        if (consecutiveFailures >= maxRestarts)
        {
            _logger.LogError("Tunnel exceeded max restarts ({Max}), giving up", maxRestarts);
            SetStatus(TunnelStatus.Disabled);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _currentSession?.Stop();
        _currentSession?.Dispose();

        try { _supervisorTask?.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }

        _cts?.Dispose();
    }
}

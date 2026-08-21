using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Tunnel;

public sealed class ShareTunnelService : IShareTunnelService, IStartable, IDisposable
{
    private readonly IConfigService _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IServer _server;
    private readonly ILogger<ShareTunnelService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _supervisorTask;
    private TunnelSession? _currentSession;
    private bool _isInstalled;
    private TunnelStatus _status = TunnelStatus.Disabled;
    private string? _errorMessage;

    public ShareTunnelService(
        IConfigService config,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime appLifetime,
        IServer server,
        ILogger<ShareTunnelService> logger)
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
    public string? ErrorMessage => _errorMessage;

    public int SharePort
    {
        get
        {
            if (_config.Settings.ShareTunnel?.Port is { } p && p > 0)
                return p;
            var basePort = _config.Settings.Tunnel?.Port ?? 5010;
            return basePort + 1;
        }
    }

    public event Action<TunnelStatus>? StatusChanged;

    private void SetStatus(TunnelStatus status)
    {
        if (_status == status) return;
        _status = status;
        StatusChanged?.Invoke(status);
    }

    public void Start()
    {
        var shareTunnelConfig = _config.Settings.ShareTunnel;
        if (shareTunnelConfig is not { Enabled: true })
        {
            _logger.LogDebug("Share tunnel is disabled, skipping");
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

        _config.Settings.ShareTunnel ??= new TunnelConfig();
        _config.Settings.ShareTunnel.Enabled = true;
        _config.Settings.ShareTunnel.Port = SharePort;
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
            try { await supervisorTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
        }

        _currentSession?.Dispose();
        _currentSession = null;
        _cts?.Dispose();
        _cts = null;
        _supervisorTask = null;

        _config.Settings.ShareTunnel ??= new TunnelConfig();
        _config.Settings.ShareTunnel.Enabled = false;
        _config.SaveSettings();

        _errorMessage = null;
        SetStatus(TunnelStatus.Disabled);
    }

    public string GetShareUrlForPlan(string planFolderName, bool isReview = true)
    {
        var appPath = isReview ? "/review" : "/drafts";
        var planQuery = $"?planId={Uri.EscapeDataString(planFolderName)}&share=1";

        if (!string.IsNullOrEmpty(TunnelUrl))
        {
            return $"{TunnelUrl.TrimEnd('/')}{appPath}{planQuery}";
        }

        return $"{appPath}{planQuery}";
    }

    private void StartSupervisor()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _errorMessage = null;
        SetStatus(TunnelStatus.Connecting);
        _supervisorTask = Task.Run(() => SupervisorLoopAsync(_cts.Token));
    }

    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HealthCheckInitialDelay = TimeSpan.FromSeconds(3);

    private async Task WaitForTunnelHealthyAsync(string tunnelUrl, CancellationToken ct)
    {
        using var http = _httpClientFactory.CreateClient("ShareTunnelHealthCheck");
        http.Timeout = TimeSpan.FromSeconds(10);

        try { await Task.Delay(HealthCheckInitialDelay, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }

        var deadline = DateTime.UtcNow + HealthCheckTimeout;
        _logger.LogInformation("Waiting for share tunnel to become routable: {Url}", tunnelUrl);

        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                using var response = await http.GetAsync(tunnelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!IsTunnelNotReady(response.StatusCode))
                {
                    _logger.LogInformation("Share tunnel is routable after {Attempts} attempt(s) (HTTP {Status})",
                        attempt, (int)response.StatusCode);
                    return;
                }

                _logger.LogDebug("Share tunnel not ready yet (HTTP {Status}), attempt {Attempt}",
                    (int)response.StatusCode, attempt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Share tunnel health check attempt {Attempt} failed: {Error}", attempt, ex.Message);
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Share tunnel did not become routable within {HealthCheckTimeout.TotalSeconds:0}s ({tunnelUrl})");
            }

            await Task.Delay(HealthCheckInterval, ct);
        }
    }

    private static bool IsTunnelNotReady(System.Net.HttpStatusCode status) =>
        status is System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.GatewayTimeout
            or (System.Net.HttpStatusCode)530;

    private async Task SupervisorLoopAsync(CancellationToken ct)
    {
        var shareTunnelConfig = _config.Settings.ShareTunnel ?? new TunnelConfig();
        var maxRestarts = shareTunnelConfig.MaxRestarts > 0 ? shareTunnelConfig.MaxRestarts : 10;

        string binaryPath;
        if (!string.IsNullOrEmpty(shareTunnelConfig.BinaryPath))
        {
            binaryPath = shareTunnelConfig.BinaryPath;
        }
        else
        {
            var installer = new CloudflaredInstaller(
                _config.TendrilHome, _httpClientFactory, _logger);
            binaryPath = await installer.EnsureInstalledAsync(ct);
        }

        if (!_appLifetime.ApplicationStarted.IsCancellationRequested)
        {
            _logger.LogInformation("Waiting for server before launching share tunnel");
            await Task.Delay(Timeout.Infinite, _appLifetime.ApplicationStarted)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (ct.IsCancellationRequested) return;
        }

        var originUrl = $"http://127.0.0.1:{SharePort}";
        _logger.LogInformation("Share tunnel origin URL: {OriginUrl}", originUrl);

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
                _logger.LogWarning("Share tunnel process exited unexpectedly");
                SetStatus(TunnelStatus.Connecting);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                _errorMessage = $"Share tunnel startup failed: {ex.Message}";
                if (ex is TimeoutException || ex.Message.Contains("api.trycloudflare.com") || ex.Message.Contains("deadline exceeded") || ex.Message.Contains("Timeout") || ex.Message.Contains("within"))
                {
                    _errorMessage += ". Network blocked 'trycloudflare.com'. Check DNS or VPN.";
                }
                SetStatus(TunnelStatus.Connecting);
                StatusChanged?.Invoke(TunnelStatus.Connecting);
                _logger.LogWarning(ex, "Share tunnel session failed (attempt {Count}/{Max})",
                    consecutiveFailures, maxRestarts);
            }
            finally
            {
                session?.Dispose();
                if (ReferenceEquals(_currentSession, session))
                    _currentSession = null;
            }

            if (ct.IsCancellationRequested) break;

            var delay = TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, consecutiveFailures - 1), 60));
            _logger.LogInformation("Restarting share tunnel in {Delay}s", delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }

        if (consecutiveFailures >= maxRestarts)
        {
            _logger.LogError("Share tunnel exceeded max restarts ({Max}), giving up", maxRestarts);
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

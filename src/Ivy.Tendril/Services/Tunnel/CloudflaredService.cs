using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Tunnel;

public sealed class CloudflaredService : ICloudflaredService, IStartable, IDisposable
{
    private readonly IConfigService _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CloudflaredService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _supervisorTask;
    private TunnelSession? _currentSession;

    public CloudflaredService(
        IConfigService config,
        IHttpClientFactory httpClientFactory,
        ILogger<CloudflaredService> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string? TunnelUrl => _currentSession?.TunnelUrl;
    public bool IsConnected => _currentSession?.IsRunning == true && TunnelUrl is not null;

    public event Action<string>? TunnelConnected;
    public event Action? TunnelDisconnected;

    public void Start()
    {
        var tunnelConfig = _config.Settings.Tunnel;
        if (tunnelConfig is not { Enabled: true })
        {
            _logger.LogDebug("Tunnel is disabled, skipping");
            return;
        }

        _cts = new CancellationTokenSource();
        _supervisorTask = Task.Run(() => SupervisorLoopAsync(_cts.Token));
    }

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

        var consecutiveFailures = 0;

        while (!ct.IsCancellationRequested && consecutiveFailures < maxRestarts)
        {
            try
            {
                _currentSession = new TunnelSession(binaryPath, tunnelConfig.Port, _logger);
                var url = await _currentSession.StartAsync(ct);
                consecutiveFailures = 0;
                TunnelConnected?.Invoke(url);

                await _currentSession.WaitForExitAsync(ct);
                _logger.LogWarning("Tunnel process exited unexpectedly");
                TunnelDisconnected?.Invoke();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                _logger.LogWarning(ex, "Tunnel session failed (attempt {Count}/{Max})",
                    consecutiveFailures, maxRestarts);
            }
            finally
            {
                _currentSession?.Dispose();
                _currentSession = null;
            }

            if (ct.IsCancellationRequested) break;

            var delay = TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, consecutiveFailures - 1), 60));
            _logger.LogInformation("Restarting tunnel in {Delay}s", delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }

        if (consecutiveFailures >= maxRestarts)
            _logger.LogError("Tunnel exceeded max restarts ({Max}), giving up", maxRestarts);
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

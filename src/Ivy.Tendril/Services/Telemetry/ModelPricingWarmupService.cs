using Ivy.Tendril.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Telemetry;

/// <summary>
/// Warms the model pricing provider up from models.dev, so computed costs and the rates shown in
/// the cost breakdown sheet track real prices rather than the hardcoded per-provider catalogs.
/// The provider is built synchronously at DI time from <c>GetStaticModels()</c> and must not make a
/// network call there, which is why this runs afterwards and off the startup path.
/// </summary>
public sealed class ModelPricingWarmupService(
    IModelPricingRefresher refresher,
    ILogger<ModelPricingWarmupService> logger) : IStartable, IDisposable
{
    // Late enough not to compete with the rest of startup for the first request; repeated because
    // the app runs for days and the underlying models.dev response is cached for 24 hours.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Period = TimeSpan.FromHours(6);

    private Timer? _timer;

    public void Start()
    {
        _timer = new Timer(_ => _ = RefreshAsync(), null, InitialDelay, Period);
    }

    /// <summary>
    /// Never throws: stale or hardcoded prices are a far better outcome than a background exception,
    /// and models.dev being unreachable is an ordinary condition.
    /// </summary>
    internal async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var updated = await refresher.RefreshFromModelsDevAsync(ct);
            if (updated > 0)
                logger.LogInformation("Refreshed {Count} model prices from models.dev", updated);
            else
                logger.LogDebug("No model prices changed from models.dev");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh model prices from models.dev; keeping current rates");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}

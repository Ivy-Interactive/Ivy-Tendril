using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test.Services;

public class ModelPricingWarmupServiceTests
{
    private sealed class FakeRefresher(int updated = 0, Exception? throws = null) : IModelPricingRefresher
    {
        public int Calls { get; private set; }

        public Task<int> RefreshFromModelsDevAsync(CancellationToken ct = default)
        {
            Calls++;
            if (throws is not null) throw throws;
            return Task.FromResult(updated);
        }
    }

    private static ModelPricingWarmupService Service(IModelPricingRefresher refresher) =>
        new(refresher, NullLogger<ModelPricingWarmupService>.Instance);

    [Fact]
    public async Task RefreshAsync_WarmsThePricingProvider()
    {
        var refresher = new FakeRefresher(updated: 12);

        await Service(refresher).RefreshAsync();

        Assert.Equal(1, refresher.Calls);
    }

    [Fact]
    public async Task RefreshAsync_ModelsDevUnreachable_DoesNotThrow()
    {
        // Runs on a background timer, so an escaping exception would take the app down over
        // something as ordinary as being offline. Stale rates are the right outcome.
        var refresher = new FakeRefresher(throws: new HttpRequestException("no network"));

        await Service(refresher).RefreshAsync();

        Assert.Equal(1, refresher.Calls);
    }

    [Fact]
    public void Dispose_WithoutStart_IsSafe()
    {
        Service(new FakeRefresher()).Dispose();
    }
}

using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;

namespace Ivy.Tendril.Agents.Test.Runtime;

public class CachedModelCatalogProviderTests
{
    [Fact]
    public async Task GetModelsAsync_NoDiscovery_ReturnsStatic()
    {
        var catalog = new TestCatalog(discoverResult: null);

        var result = await catalog.GetModelsAsync();

        Assert.Equal(ModelCatalogSource.Static, result.Source);
        Assert.Equal("test", result.AgentId);
        Assert.Equal(2, result.Models.Count);
    }

    [Fact]
    public async Task GetModelsAsync_WithDiscovery_ReturnsDynamic()
    {
        var discovered = new List<ModelInfo>
        {
            new() { Id = "discovered-1", DisplayName = "Discovered 1", Provider = "test" },
        };
        var catalog = new TestCatalog(discoverResult: discovered);

        var result = await catalog.GetModelsAsync();

        Assert.Equal(ModelCatalogSource.Dynamic, result.Source);
        Assert.Single(result.Models);
        Assert.Equal("discovered-1", result.Models[0].Id);
    }

    [Fact]
    public async Task GetModelsAsync_CachesResult_ReturnsCachedOnSecondCall()
    {
        var discovered = new List<ModelInfo>
        {
            new() { Id = "discovered-1", DisplayName = "Discovered 1", Provider = "test" },
        };
        var catalog = new TestCatalog(discoverResult: discovered);

        var first = await catalog.GetModelsAsync();
        Assert.Equal(ModelCatalogSource.Dynamic, first.Source);

        var second = await catalog.GetModelsAsync();
        Assert.Equal(ModelCatalogSource.Cached, second.Source);
        Assert.Equal(1, catalog.DiscoverCallCount);
    }

    [Fact]
    public async Task GetModelsAsync_DiscoveryThrows_FallsBackToStatic()
    {
        var catalog = new TestCatalog(discoverResult: null, throwOnDiscover: true);

        var result = await catalog.GetModelsAsync();

        Assert.Equal(ModelCatalogSource.Static, result.Source);
        Assert.Equal(2, result.Models.Count);
    }

    private class TestCatalog : CachedModelCatalogProvider
    {
        private readonly IReadOnlyList<ModelInfo>? _discoverResult;
        private readonly bool _throwOnDiscover;

        public int DiscoverCallCount { get; private set; }

        public TestCatalog(IReadOnlyList<ModelInfo>? discoverResult, bool throwOnDiscover = false)
            : base(TimeSpan.FromHours(1))
        {
            _discoverResult = discoverResult;
            _throwOnDiscover = throwOnDiscover;
        }

        public override string AgentId => "test";

        public override IReadOnlyList<ModelInfo> GetStaticModels() =>
        [
            new() { Id = "static-1", DisplayName = "Static 1", Provider = "test", IsDefault = true, InputPerMillion = 1m, OutputPerMillion = 2m },
            new() { Id = "static-2", DisplayName = "Static 2", Provider = "test", InputPerMillion = 0.5m, OutputPerMillion = 1m },
        ];

        protected override Task<IReadOnlyList<ModelInfo>?> DiscoverModelsAsync(CancellationToken ct)
        {
            DiscoverCallCount++;
            if (_throwOnDiscover)
                throw new InvalidOperationException("Discovery failed");
            return Task.FromResult(_discoverResult);
        }
    }
}

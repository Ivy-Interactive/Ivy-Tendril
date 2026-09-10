using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services.Telemetry;

namespace Ivy.Tendril.Test.Services;

public class AgentUsageServiceTests
{
    [Fact]
    public async Task SecondCallInsideTtl_DoesNotReinvokeProvider_CallAfterTtlDoes()
    {
        var runner = new FakeAgentRunner();
        var timeProvider = new FakeTimeProvider();
        var callCount = 0;

        var snapshot = new AgentUsageSnapshot
        {
            AgentId = "test-agent",
            Windows =
            [
                new AgentUsageWindow { WindowMinutes = 300, UsedPercent = 10.0 }
            ]
        };

        var provider = new FakeUsageProvider("test-agent", () =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<AgentUsageSnapshot?>(snapshot);
        });
        runner.Register("test-agent", provider);

        var service = new AgentUsageService(
            runner,
            timeProvider: timeProvider,
            ttl: TimeSpan.FromSeconds(60));

        // Call 1: invokes provider
        var result1 = await service.GetUsageAsync("test-agent");
        Assert.NotNull(result1);
        Assert.Equal(1, callCount);

        // Call 2 (inside TTL, +30 seconds): returns cached, provider not reinvoked
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(30);
        var result2 = await service.GetUsageAsync("test-agent");
        Assert.NotNull(result2);
        Assert.Equal(1, callCount);

        // Call 3 (after TTL, +61 seconds total): reinvokes provider
        timeProvider.UtcNow = timeProvider.UtcNow.AddSeconds(31);
        var result3 = await service.GetUsageAsync("test-agent");
        Assert.NotNull(result3);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ProviderThatThrows_YieldsNull_NotAnException()
    {
        var runner = new FakeAgentRunner();
        var provider = new FakeUsageProvider("throwing-agent", () => throw new InvalidOperationException("API down"));
        runner.Register("throwing-agent", provider);

        var service = new AgentUsageService(runner);
        var result = await service.GetUsageAsync("throwing-agent");

        Assert.Null(result);
    }

    [Fact]
    public async Task AgentWithNoRegisteredProvider_YieldsNull()
    {
        var runner = new FakeAgentRunner();
        var service = new AgentUsageService(runner);

        var result = await service.GetUsageAsync("unregistered-agent");

        Assert.Null(result);
    }

    private sealed class FakeUsageProvider(string agentId, Func<Task<AgentUsageSnapshot?>> getUsage) : IAgentUsageProvider
    {
        public string AgentId => agentId;
        public Task<AgentUsageSnapshot?> GetUsageAsync(CancellationToken ct = default) => getUsage();
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeAgentRunner : IAgentRunner
    {
        private readonly Dictionary<string, IAgentUsageProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string agentId, IAgentUsageProvider provider) => _providers[agentId] = provider;

        public IAgentUsageProvider? GetUsageProvider(string agentId) => _providers.GetValueOrDefault(agentId);

        public Task<IAgentSession> LaunchAsync(AgentResolutionContext context, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ResultEvent> RunToCompletionAsync(AgentResolutionContext context, CancellationToken ct = default) => throw new NotImplementedException();
        public IReadOnlyList<IAgentSession> ActiveSessions => [];
        public IObservable<IAgentSession> Sessions => System.Reactive.Linq.Observable.Empty<IAgentSession>();
        public Task StopAllAsync(CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<string> RegisteredAgents => _providers.Keys.ToList();
        public IAgentCli GetCli(string agentId) => throw new NotImplementedException();
        public IEventParser GetParser(string agentId) => throw new NotImplementedException();
        public IAgentHealthCheck GetHealthCheck(string agentId) => throw new NotImplementedException();
        public IAgentDescriptor GetDescriptor(string agentId) => throw new NotImplementedException();
        public IFailureAnalyzer? GetFailureAnalyzer(string agentId) => null;
        public ISessionCostParser? GetCostParser(string agentId) => null;
        public IAgentPty? GetPty(string agentId) => null;
        public IModelCatalogProvider? GetModelCatalog(string agentId) => null;
        public IEnumerable<IModelCatalogProvider> ModelCatalogs => [];
    }
}

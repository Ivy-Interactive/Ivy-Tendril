using Ivy.Tendril.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Services.Telemetry;

public sealed class AgentUsageService
{
    private readonly IAgentRunner _runner;
    private readonly ILogger<AgentUsageService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Dictionary<string, (AgentUsageSnapshot? Snapshot, DateTimeOffset CachedAt)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public AgentUsageService(
        IAgentRunner runner,
        ILogger<AgentUsageService>? logger = null,
        TimeProvider? timeProvider = null,
        TimeSpan? ttl = null)
    {
        _runner = runner;
        _logger = logger ?? NullLogger<AgentUsageService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? TimeSpan.FromSeconds(60);
    }

    public async Task<AgentUsageSnapshot?> GetUsageAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return null;

        var now = _timeProvider.GetUtcNow();

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(agentId, out var entry))
            {
                if (now - entry.CachedAt < _ttl)
                    return entry.Snapshot;
            }

            var provider = _runner.GetUsageProvider(agentId);
            if (provider == null)
            {
                return null;
            }

            AgentUsageSnapshot? snapshot = null;
            try
            {
                snapshot = await provider.GetUsageAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch usage for agent {AgentId}", agentId);
                return null;
            }

            _cache[agentId] = (snapshot, now);
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving usage for agent {AgentId}", agentId);
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

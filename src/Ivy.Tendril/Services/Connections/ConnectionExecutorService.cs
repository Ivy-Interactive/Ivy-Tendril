using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Connections;

namespace Ivy.Tendril.Services;

public interface IConnectionExecutorService
{
    Task<(bool Success, string Result)> ExecuteActionAsync(ConnectionItem connection, string action, string argsJson);
    Task<(bool Success, string ErrorMessage)> TestConnectionAsync(ConnectionItem connection);
}

public class ConnectionExecutorService : IConnectionExecutorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Dictionary<string, IConnectionProvider> _providers;

    public ConnectionExecutorService(
        IHttpClientFactory httpClientFactory,
        IEnumerable<IConnectionProvider> providers)
    {
        _httpClientFactory = httpClientFactory;
        _providers = providers.ToDictionary(p => p.ProviderName, p => p, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(bool Success, string ErrorMessage)> TestConnectionAsync(ConnectionItem connection)
    {
        if (!_providers.TryGetValue(connection.Provider, out var provider))
        {
            return (false, $"Unknown integration provider: {connection.Provider}");
        }

        using var client = _httpClientFactory.CreateClient();
        return await provider.TestConnectionAsync(connection.ConnectionString, client);
    }

    public async Task<(bool Success, string Result)> ExecuteActionAsync(ConnectionItem connection, string action, string argsJson)
    {
        if (!_providers.TryGetValue(connection.Provider, out var provider))
        {
            return (false, $"Unknown integration provider: {connection.Provider}");
        }

        using var client = _httpClientFactory.CreateClient();
        return await provider.ExecuteActionAsync(connection.ConnectionString, action, argsJson, client);
    }
}

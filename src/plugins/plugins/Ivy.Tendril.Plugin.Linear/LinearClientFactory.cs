using Ivy.Tendril.Plugin.Linear.GraphQL;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Tendril.Plugin.Linear;

public sealed class LinearClientFactory : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public ILinearGraphQLClient Client { get; }

    public LinearClientFactory(string apiKey)
    {
        var services = new ServiceCollection();

        services
            .AddLinearGraphQLClient()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri("https://api.linear.app/graphql");
                client.DefaultRequestHeaders.Add("Authorization", apiKey);
            });

        _serviceProvider = services.BuildServiceProvider();
        Client = _serviceProvider.GetRequiredService<ILinearGraphQLClient>();
    }

    public void Dispose() => _serviceProvider.Dispose();
}

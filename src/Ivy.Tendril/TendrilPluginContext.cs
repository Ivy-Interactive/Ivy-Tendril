using Ivy.Core.Apps;
using Ivy.Core.Plugins;
using Ivy.Plugins;
using Ivy.Plugins.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Tendril;

internal class TendrilPluginContext(Server server, WebApplicationBuilder builder)
    : PluginContextBase, ITendrilPluginContext
{
    protected override IConfiguration BaseConfiguration => server.Configuration;
    protected override AppRepository AppRepository => server.AppRepository;
    protected override IReadOnlySet<string> ReservedPaths => server.ReservedPaths;
    protected override WebApplicationBuilder Builder => builder;

    public void RegisterMessagingChannel(IMessagingChannel channel)
    {
        Services.AddSingleton<IMessagingChannel>(channel);
    }
}

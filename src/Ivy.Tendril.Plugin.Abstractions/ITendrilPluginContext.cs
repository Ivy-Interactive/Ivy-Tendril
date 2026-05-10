using Ivy.Plugins.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Plugins;

public interface ITendrilPluginContext : IPluginContext
{
    void RegisterMessagingChannel(IMessagingChannel channel)
    {
        Services.AddSingleton(channel);
    }
}

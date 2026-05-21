using Ivy.Plugins.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Plugins;

public interface ITendrilPluginContext : IIvyPluginContext
{
    string TendrilHome { get; }

    void RegisterMessagingChannel(IMessagingChannel channel)
    {
        Services.AddSingleton(channel);
    }
}

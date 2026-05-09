using Ivy.Plugins.Messaging;

namespace Ivy.Plugins;

public interface ITendrilPluginContext : IPluginContext
{
    void RegisterMessagingChannel(IMessagingChannel channel);
}

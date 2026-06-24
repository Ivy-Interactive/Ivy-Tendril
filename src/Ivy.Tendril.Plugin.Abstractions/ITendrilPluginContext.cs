using Ivy.Plugins.Hooks;
using Ivy.Plugins.Inbox;
using Ivy.Plugins.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Plugins;

public interface ITendrilPluginContext : IIvyPluginContext
{
    string TendrilHome { get; }
    IInbox Inbox { get; }
    IPluginHooks Hooks { get; }

    void RegisterMessagingChannel(IMessagingChannel channel)
    {
        Services.AddSingleton(channel);
    }
}

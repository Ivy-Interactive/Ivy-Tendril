using Ivy.Plugins.Hooks;
using Ivy.Plugins.Inbox;
using Ivy.Plugins.Messaging;
using Ivy.Plugins.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Plugins;

public interface ITendrilPluginContext : IIvyPluginContext
{
    string TendrilHome { get; }
    IInbox Inbox { get; }
    IPluginHooks Hooks { get; }
    ISourceLinks SourceLinks { get; }

    void RegisterMessagingChannel(IMessagingChannel channel)
    {
        Services.AddSingleton(channel);
    }
}

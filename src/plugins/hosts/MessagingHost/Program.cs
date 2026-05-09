using Ivy;
using Ivy.Core.Plugins;
using Ivy.Plugins.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var server = new Server();
server.UseAppShell(new AppShellSettings());
server.AddAppsFromAssembly(typeof(Program).Assembly);

var pluginsDir = Path.GetFullPath(
    Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plugins"));

server.UsePlugins(pluginsDir,
    contextFactory: (s, builder) => new MessagingPluginContext(s, builder),
    sharedAssemblyNames: ["Ivy.Tendril.Plugin.Abstractions"]);

await server.RunAsync();

class MessagingPluginContext(Server server, WebApplicationBuilder builder)
    : PluginContextBase, Ivy.Plugins.ITendrilPluginContext
{
    protected override IConfiguration BaseConfiguration => server.Configuration;
    protected override Ivy.Core.Apps.AppRepository AppRepository => server.AppRepository;
    protected override IReadOnlySet<string> ReservedPaths => server.ReservedPaths;
    protected override WebApplicationBuilder Builder => builder;

    public void RegisterMessagingChannel(IMessagingChannel channel)
    {
        Services.AddSingleton<IMessagingChannel>(channel);
    }
}

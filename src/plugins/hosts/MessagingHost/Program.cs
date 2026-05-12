using Ivy;
using Ivy.Core.Plugins;
using Ivy.Plugins.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var server = new Server();
server.UseAppShell(new AppShellSettings());
server.AddAppsFromAssembly(typeof(Program).Assembly);

var pluginsDir = Path.GetFullPath(
    Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plugins"));

server.UsePlugins(pluginsDir,
    contextFactory: (s, builder) => new MessagingPluginContext(s, builder),
    sharedAssemblyNames: ["Ivy.Tendril.Plugin.Abstractions"],
    buildSourcePlugins: true);

await server.RunAsync();

class MessagingPluginContext(Server server, WebApplicationBuilder builder)
    : PluginContextBase(server, builder), Ivy.Plugins.ITendrilPluginContext
{
}

using Ivy;
using Ivy.Core.Plugins;
using Ivy.Plugins;
using Ivy.Plugins.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var server = new Server();
server.UseAppShell(new AppShellSettings());
server.AddAppsFromAssembly(typeof(Program).Assembly);

var pluginsDir = Path.GetFullPath(
    Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plugins"));

server.UsePlugins(pluginsDir,
    new MessagingPluginConfigFactory(pluginsDir),
    contextFactory: (s, builder) => new MessagingPluginContext(s, builder),
    sharedAssemblyNames: ["Ivy.Tendril.Plugin.Abstractions"],
    buildSourcePlugins: true);

await server.RunAsync();

class MessagingPluginContext(Server server, WebApplicationBuilder builder)
    : PluginContextBase(server, builder), Ivy.Plugins.ITendrilPluginContext
{
    public string TendrilHome { get; } = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
}

class MessagingPluginConfigFactory(string pluginsDir) : IIvyPluginConfigFactory
{
    private readonly string _configPath = Path.Combine(pluginsDir, "plugin-config.yaml");

    public IIvyPluginConfig Create(string pluginId) => new MessagingPluginConfig(_configPath, pluginId);
}

class MessagingPluginConfig(string configPath, string pluginId) : IIvyPluginConfig
{
    public string? GetValue(string key)
    {
        if (!File.Exists(configPath)) return null;
        var yaml = File.ReadAllText(configPath);
        if (string.IsNullOrWhiteSpace(yaml)) return null;
        var data = new YamlDotNet.Serialization.DeserializerBuilder().Build()
            .Deserialize<Dictionary<object, object>>(yaml);
        if (data?.TryGetValue(pluginId, out var section) == true
            && section is Dictionary<object, object> dict
            && dict.TryGetValue(key, out var value))
            return value?.ToString();
        return null;
    }

    public void SetValue(string key, string value) { }
    public void RemoveValue(string key) { }
    public void Save() { }
}

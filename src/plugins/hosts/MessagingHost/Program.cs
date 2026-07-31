using Ivy;
using Ivy.Core.Plugins;
using Ivy.Plugins;
using Ivy.Plugins.Hooks;
using Ivy.Plugins.Inbox;
using Ivy.Plugins.Messaging;
using Ivy.Plugins.Sources;
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

    // This harness exists to exercise messaging channels outside a running Tendril, so the rest of
    // the plugin context is stubbed — a plugin can call these, they just do nothing here.
    public IInbox Inbox { get; } = new NoOpInbox();
    public IPluginHooks Hooks { get; } = new NoOpHooks();
    public ISourceLinks SourceLinks { get; } = new NoOpSourceLinks();
}

class NoOpInbox : IInbox
{
    public void Add(string description) { }
    public void Add(InboxItem item) { }
    public void AddRange(IEnumerable<InboxItem> items) { }
}

class NoOpHooks : IPluginHooks
{
    public void BeforeJob(Func<BeforeJobEvent, CancellationToken, Task> handler) { }
    public void AfterJob(Func<AfterJobEvent, CancellationToken, Task> handler) { }
    public void BeforeCreatePlan(Func<BeforeCreatePlanEvent, CancellationToken, Task> handler) { }
    public void AfterCreatePlan(Func<AfterCreatePlanEvent, CancellationToken, Task> handler) { }
    public void BeforeConfigSave(Action<ConfigSaveEvent> handler) { }
    public void AfterConfigReload(Action handler) { }
}

class NoOpSourceLinks : ISourceLinks
{
    public void RegisterResolver(Func<Uri, string?> resolver) { }
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

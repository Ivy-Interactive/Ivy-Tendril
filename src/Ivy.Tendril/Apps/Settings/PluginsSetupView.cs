using Ivy.Apps;
using Ivy.Plugins;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

internal static class PluginIconHelper
{
    public static object? ToWidget(PluginIcon? icon, string? pluginId = null)
    {
        if (icon is null) return null;
        return icon.Kind switch
        {
            PluginIconKind.Named => IconsHelper.FromString(icon.Value) is { } parsed
                ? new Icon(parsed)
                : null,
            PluginIconKind.Url => new Image(icon.Value).Width(Size.Units(5)).Height(Size.Units(5)),
            PluginIconKind.File when pluginId is not null =>
                new Image($"/ivy/plugins/{pluginId}/assets/{icon.Value}").Width(Size.Units(5)).Height(Size.Units(5)),
            _ => null
        };
    }
}

public class PluginsSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var pluginManager = UseService<IPluginManager>();
        var configFactory = UseService<IIvyPluginConfigFactory>();
        UsePluginState();

        var activePlugins = pluginManager.GetActivePluginIds();
        var unconfiguredPlugins = pluginManager.GetUnconfiguredPlugins();
        var unloadedPlugins = pluginManager.GetUnloadedPlugins();
        var pluginsDir = Path.Combine(config.TendrilHome, "plugins");

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Plugins").Bold()
               | Text.Block("Manage installed Tendril plugins. Plugins are loaded from the plugins directory.").Muted().Small()
               | Text.Block(pluginsDir).Muted().Small()
               | new Separator()
               | Text.Block("Active Plugins").Bold()
               | (activePlugins.Count == 0
                   ? (object)Text.Block("No plugins currently active.").Muted()
                   : activePlugins.Select(id =>
                   {
                       var manifest = pluginManager.GetPluginManifest(id);
                       var schema = pluginManager.GetPluginSchema(id);
                       var pluginConfig = configFactory.Create(id);
                       var customView = pluginManager.BuildPluginConfigurationView(id, pluginConfig);
                       var header = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                           | PluginIconHelper.ToWidget(manifest?.Icon, id)
                           | new Badge(manifest?.Name ?? id, BadgeVariant.Secondary);
                       var content = Layout.Vertical().Gap(3)
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                               | new Button("Reload", onClick: _ =>
                               {
                                   var success = pluginManager.ReloadPlugin(id);
                                   client.Toast(success ? $"Reloaded '{id}'" : $"Failed to reload '{id}'",
                                       success ? "Reloaded" : "Error");
                                   return ValueTask.CompletedTask;
                               }, variant: ButtonVariant.Outline, icon: Icons.RefreshCw)
                               | new Button("Unload", onClick: _ =>
                               {
                                   var success = pluginManager.UnloadPlugin(id);
                                   client.Toast(success ? $"Unloaded '{id}'" : $"Failed to unload '{id}'",
                                       success ? "Unloaded" : "Error");
                                   return ValueTask.CompletedTask;
                               }, variant: ButtonVariant.Outline, icon: Icons.Power))
                           | (customView
                               ?? (schema is not null
                                   ? new PluginConfigurationView(id, schema, configFactory)
                                   : null));
                       return (object)new Expandable(header, content);
                   }).ToArray())
               | new Separator()
               | Text.Block("Unconfigured Plugins").Bold()
               | (unconfiguredPlugins.Count == 0
                   ? (object)Text.Block("No unconfigured plugins.").Muted()
                   : unconfiguredPlugins.Select(p =>
                   {
                       var manifest = pluginManager.GetPluginManifest(p.Id);
                       var pluginConfig = configFactory.Create(p.Id);
                       var customView = pluginManager.BuildPluginConfigurationView(p.Id, pluginConfig);
                       var header = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                           | PluginIconHelper.ToWidget(manifest?.Icon, p.Id)
                           | new Badge(p.Name, BadgeVariant.Warning)
                           | Text.Block(string.Join(", ", p.ValidationErrors)).Muted().Small();
                       var content = customView ?? new PluginConfigurationView(p.Id, p.Schema, configFactory);
                       return (object)new Expandable(header, content);
                   }).ToArray())
               | new Separator()
               | Text.Block("Unloaded Plugins").Bold()
               | (unloadedPlugins.Count == 0
                   ? (object)Text.Block("No unloaded plugins found.").Muted()
                   : unloadedPlugins.Select(p => (object)(Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                       | new Badge(p.Id, p.FailureReason is not null ? BadgeVariant.Destructive : BadgeVariant.Outline)
                       | (p.FailureReason is not null ? (object)Text.Block(p.FailureReason).Muted().Small() : null!)
                       | new Button(p.FailureReason is not null ? "Retry" : "Install", onClick: _ =>
                       {
                           var success = pluginManager.LoadPlugin(p.Directory);
                           client.Toast(success ? $"Loaded '{p.Id}'" : $"Failed to load '{p.Id}'",
                               success ? "Installed" : "Error");
                           return ValueTask.CompletedTask;
                       }, variant: ButtonVariant.Outline, icon: p.FailureReason is not null ? Icons.RefreshCw : Icons.Plus)
                   )).ToArray())
               | new Separator()
               | Layout.Horizontal().Gap(2)
                   | new Button("Open Plugins Folder", onClick: _ =>
                   {
                       config.OpenInEditor(pluginsDir);
                       return ValueTask.CompletedTask;
                   }, variant: ButtonVariant.Outline, icon: Icons.FolderOpen);
    }
}

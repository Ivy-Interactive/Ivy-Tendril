using System.IO.Compression;
using System.Net.Http.Json;
using Ivy.Apps;
using Ivy.Plugins;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

internal static class PluginIconHelper
{
    private static readonly Size IconSize = Size.Units(4);

    public static object? ToWidget(PluginIcon? icon, string? pluginId = null)
    {
        if (icon is null) return null;
        return icon.Kind switch
        {
            PluginIconKind.Named => IconsHelper.FromString(icon.Value) is { } parsed
                ? new Icon(parsed)
                : null,
            PluginIconKind.Url => new Image(icon.Value).Width(IconSize).Height(IconSize),
            PluginIconKind.File when pluginId is not null =>
                new Image($"/ivy/plugins/{pluginId}/assets/{icon.Value}").Width(IconSize).Height(IconSize),
            _ => null
        };
    }

    public static Icon UnloadedIcon() => new Icon(Icons.Unplug).Width(IconSize).Height(IconSize);
}

public class PluginsSetupView : ViewBase
{
    private record AvailablePlugin(string PackageId, string Version, string Hash, string Title, string? Description, string? IconUrl, string? ProjectUrl);

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var pluginManager = UseService<IPluginManager>();
        var configFactory = UseService<IIvyPluginConfigFactory>();
        var tendrilArgs = UseService<TendrilArgs>();
        var httpClientFactory = UseService<IHttpClientFactory>();
        UsePluginState();
        var availableQuery = UseQuery(
            key: "availablePlugins",
            fetcher: async ct =>
            {
                using var http = httpClientFactory.CreateClient();
                return await http.GetFromJsonAsync<AvailablePlugin[]>(
                    $"{tendrilArgs.ServicesUrl}/plugins", ct) ?? [];
            }
        );

        var activePlugins = pluginManager.GetActivePluginIds();
        var unconfiguredPlugins = pluginManager.GetUnconfiguredPlugins();
        var unloadedPlugins = pluginManager.GetUnloadedPlugins();
        var pluginsDir = Path.Combine(config.TendrilHome, "plugins");

        var installedPackageIds = activePlugins
            .Concat(unconfiguredPlugins.Select(p => p.Id))
            .Concat(unloadedPlugins.Select(p => p.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var availablePlugins = availableQuery.Value?
            .Where(p => !installedPackageIds.Contains(p.PackageId))
            .ToArray();

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Plugins").Bold()
               | Text.Block("Manage and configure Tendril plugins.").Muted().Small()
               | new Separator()
               | (activePlugins.Count == 0 && unconfiguredPlugins.Count == 0
                   ? (object)Text.Block("No plugins loaded.").Muted()
                   : activePlugins.Select(id =>
                   {
                       var manifest = pluginManager.GetPluginManifest(id);
                       var schema = pluginManager.GetPluginSchema(id);
                       var pluginConfig = configFactory.Create(id);
                       var customView = pluginManager.BuildPluginConfigurationView(id, pluginConfig);
                       var header = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                           | PluginIconHelper.ToWidget(manifest?.Icon, id)
                           | Text.Block(manifest?.Title ?? id);
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
                                   ? new PluginConfigurationView(id, schema, configFactory).Key(id)
                                   : null));
                       return (object)new Expandable(header, content) { Key = id };
                   }).Concat(unconfiguredPlugins.Select(p =>
                   {
                       var manifest = pluginManager.GetPluginManifest(p.Id);
                       var pluginConfig = configFactory.Create(p.Id);
                       var customView = pluginManager.BuildPluginConfigurationView(p.Id, pluginConfig);
                       var header = Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween)
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                               | PluginIconHelper.ToWidget(manifest?.Icon, p.Id)
                               | Text.Block(p.Title))
                           | (Layout.Horizontal().Gap(1).AlignContent(Align.Right)
                               | Text.Block("Unconfigured").Muted().Small()
                               | new Icon(Icons.TriangleAlert, Colors.Warning));
                       var content = Layout.Vertical().Gap(3)
                           | Text.Block(string.Join(", ", p.ValidationErrors)).Muted().Small()
                           | (customView ?? new PluginConfigurationView(p.Id, p.Schema, configFactory).Key(p.Id));
                       return (object)new Expandable(header, content) { Key = p.Id };
                   })).ToArray())
               | (unloadedPlugins.Count == 0 ? null! :
                   (object)(Layout.Vertical().Gap(4)
                   | new Separator()
                   | Text.Block("Unloaded Plugins").Bold()
                   | unloadedPlugins.Select(p =>
                   {
                       var header = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                           | PluginIconHelper.UnloadedIcon()
                           | Text.Block(p.Id);
                       var content = Layout.Vertical().Gap(2)
                           | (p.FailureReason is not null ? (object)Text.Block(p.FailureReason).Muted().Small() : null!)
                           | new Button(p.FailureReason is not null ? "Retry" : "Load", onClick: _ =>
                           {
                               var success = pluginManager.LoadPlugin(p.Directory);
                               client.Toast(success ? $"Loaded '{p.Id}'" : $"Failed to load '{p.Id}'",
                                   success ? "Installed" : "Error");
                               return ValueTask.CompletedTask;
                           }, variant: ButtonVariant.Outline, icon: p.FailureReason is not null ? Icons.RefreshCw : Icons.Plus);
                       return (object)new Expandable(header, content).Open();
                   }).ToArray()))
               | new Separator()
               | BuildAvailablePluginsSection(availableQuery.Loading, availablePlugins, pluginsDir, client)
               | new Separator()
               | Layout.Horizontal().Gap(2)
                   | new Button("Open Plugins Folder", onClick: _ =>
                   {
                       PlatformHelper.OpenInFileManager(pluginsDir);
                       return ValueTask.CompletedTask;
                   }, variant: ButtonVariant.Outline, icon: Icons.FolderOpen);
    }

    private static object? BuildAvailablePluginsSection(
        bool loading, AvailablePlugin[]? plugins, string pluginsDir, IClientProvider client)
    {
        if (loading)
            return Layout.Vertical().Gap(4)
                   | Text.Block("Available Plugins").Bold()
                   | Text.Block("Loading...").Muted();

        if (plugins == null || plugins.Length == 0)
            return null;

        return Layout.Vertical().Gap(4)
               | Text.Block("Available Plugins").Bold()
               | Text.Block("Plugins approved and ready to install.").Muted().Small()
               | plugins.Select(p =>
               {
                   var header = Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween)
                       | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                           | (p.IconUrl is not null
                               ? (object)new Image(p.IconUrl).Width(Size.Units(4)).Height(Size.Units(4))
                               : new Icon(Icons.Plug))
                           | Text.Block(p.Title))
                       | new Badge(p.Version, BadgeVariant.Secondary);
                   var content = Layout.Vertical().Gap(2)
                       | (p.Description is not null ? (object)Text.Block(p.Description).Muted().Small() : null!)
                       | new Button("Install", onClick: async _ =>
                       {
                           try
                           {
                               await InstallPluginAsync(p, pluginsDir);
                               client.Toast($"Installed '{p.Title}'", "Installed");
                           }
                           catch (Exception ex)
                           {
                               client.Toast($"Failed to install: {ex.Message}", "Error");
                           }
                       }, variant: ButtonVariant.Outline, icon: Icons.Download);
                   return (object)new Expandable(header, content) { Key = p.PackageId };
               }).ToArray();
    }

    private static async Task InstallPluginAsync(AvailablePlugin plugin, string pluginsDir)
    {
        var pluginDir = Path.Combine(pluginsDir, plugin.PackageId);
        Directory.CreateDirectory(pluginDir);

        using var http = new HttpClient();
        var packageId = plugin.PackageId.ToLowerInvariant();
        var version = plugin.Version.ToLowerInvariant();
        var nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageId}/{version}/{packageId}.{version}.nupkg";

        var nupkgBytes = await http.GetByteArrayAsync(nupkgUrl);

        using var archive = new ZipArchive(new MemoryStream(nupkgBytes));
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

            var fileName = Path.GetFileName(entry.FullName);
            var destPath = Path.Combine(pluginDir, fileName);
            await using var entryStream = entry.Open();
            await using var fileStream = File.Create(destPath);
            await entryStream.CopyToAsync(fileStream);
        }
    }
}

using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Ivy.Apps;
using Ivy.Core.Plugins;
using Ivy.Desktop;
using Ivy.Plugins;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

internal static class PluginIconHelper
{
    internal static readonly Size IconSize = Size.Units(5);

    public static object? ToWidget(PluginIcon? icon)
    {
        if (icon is null) return null;
        return icon.Kind switch
        {
            PluginIconKind.Named => IconsHelper.FromString(icon.Value) is { } parsed
                ? new Icon(parsed).Width(IconSize).Height(IconSize)
                : null,
            PluginIconKind.Url => new Image(icon.Value).Width(IconSize).Height(IconSize),
            _ => null
        };
    }

    public static PluginIcon? FromApiResponse(string? iconKind, string? iconValue, string? fallbackIconUrl)
    {
        if (iconKind is not null && iconValue is not null)
        {
            return iconKind.ToLowerInvariant() switch
            {
                "named" => PluginIcon.Named(iconValue),
                "url" => PluginIcon.Url(iconValue),
                _ => fallbackIconUrl is not null ? PluginIcon.Url(fallbackIconUrl) : null
            };
        }
        return fallbackIconUrl is not null ? PluginIcon.Url(fallbackIconUrl) : null;
    }

    public static Icon UnloadedIcon() => new Icon(Icons.Unplug).Width(IconSize).Height(IconSize);
}

public class PluginsSetupView : ViewBase
{
    private record AvailablePlugin(string PackageId, string Version, string Hash, string Title, string? Description, string? IconUrl, string? IconKind, string? IconValue, string? ProjectUrl);

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var pluginManager = UseService<IPluginManager>();
        var configFactory = UseService<IIvyPluginConfigFactory>();
        var uninstallService = UseService<PluginUninstallService>();
        var dependencyResolver = UseService<NuGetDependencyResolver>();
        var updateService = UseService<IPluginUpdateService>();
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
        var updatesQuery = UseQuery(
            key: "pluginUpdates",
            fetcher: async _ => await updateService.CheckForUpdatesAsync()
        );
        // Shared state: maps packageId → progress (0-100) for currently-installing plugins
        var installingPlugins = UseState(new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>());
        // Shared state: maps packageId → progress (0-100) for currently-updating plugins
        var updatingPlugins = UseState(new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>());

        var activePlugins = pluginManager.GetActivePluginIds();
        var unconfiguredPlugins = pluginManager.GetUnconfiguredPlugins();
        var unloadedPlugins = pluginManager.GetUnloadedPlugins();
        var pluginsDir = Path.Combine(config.TendrilHome, "plugins");
        var pluginDirectories = (pluginManager as PluginLoader)?.Plugins
            .ToDictionary(p => p.Instance.Manifest.Id, p => p.Directory)
            ?? new Dictionary<string, string>();

        var installedPackageIds = activePlugins
            .Concat(unconfiguredPlugins.Select(p => p.Id))
            .Concat(unloadedPlugins.Select(p => p.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var availablePlugins = availableQuery.Value?
            .Where(p => !installedPackageIds.Contains(p.PackageId))
            .ToArray();

        var hasAnyPlugins = activePlugins.Count > 0 || unconfiguredPlugins.Count > 0 || unloadedPlugins.Count > 0;

        if (!hasAnyPlugins && installingPlugins.Value.Count == 0)
        {
            return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Plugins").Bold()
                   | (Layout.Vertical().Gap(3).AlignContent(Align.Center).Padding(6)
                       | new Icon(Icons.Plug, Colors.Muted).Width(Size.Units(10)).Height(Size.Units(10))
                       | Text.Block("No plugins installed").Bold()
                       | Text.Block("Get started by adding your first plugin.").Muted().Small()
                       | new AddPluginsDialogView(availableQuery.Loading, availablePlugins, pluginsDir, client, dependencyResolver, installingPlugins, pluginManager, httpClientFactory, ButtonVariant.Primary));
        }

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Plugins").Bold()
               | Text.Block("Manage and configure Tendril plugins.").Muted().Small()
               | (activePlugins.Count == 0 && unconfiguredPlugins.Count == 0
                   ? null!
                   : (object)activePlugins.Select(id =>
                   {
                       var manifest = pluginManager.GetPluginManifest(id);
                       var schema = pluginManager.GetPluginSchema(id);
                       var pluginConfig = configFactory.Create(id);
                       var customView = pluginManager.BuildPluginConfigurationView(id, pluginConfig);
                       var updateInfo = updatesQuery.Value?.FirstOrDefault(u =>
                           u.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase) && u.HasUpdate);
                       var isUpdating = updatingPlugins.Value.ContainsKey(id);
                       var hasUpdateBadge = updateInfo != null && !isUpdating;
                       var header = Layout.Horizontal().Gap(2).AlignContent(hasUpdateBadge ? Align.SpaceBetween : Align.Left)
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                               | PluginIconHelper.ToWidget(manifest?.Icon)
                               | Text.Block(manifest?.Title ?? id))
                           | (hasUpdateBadge
                               ? (Layout.Horizontal().Gap(1).AlignContent(Align.Right)
                                   | new Badge($"v{updateInfo!.LatestVersion}", BadgeVariant.Secondary)
                                   | new Icon(Icons.ArrowUp, Colors.Primary))
                               : null!);
                       var content = Layout.Vertical().Gap(3)
                           | (isUpdating
                               ? (object)new Progress(updatingPlugins.Value[id].Progress)
                               : updateInfo != null
                                   ? (object)(Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween)
                                       | Text.Block($"v{updateInfo.LatestVersion} available").Muted().Small()
                                       | new Button("Update", onClick: async _ =>
                                       {
                                           var title = manifest?.Title ?? id;
                                           var icon = manifest?.Icon;
                                           var showLoadingCts = new CancellationTokenSource();
                                           var loadingVisible = false;

                                           var progress = new Progress<int>(pct =>
                                           {
                                               if (!loadingVisible) return;
                                               updatingPlugins.Set(dict => new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>(dict)
                                                   { [id] = (title, icon, pct) });
                                           });

                                           #pragma warning disable CS4014
                                           Task.Delay(400, showLoadingCts.Token).ContinueWith(__ =>
                                           {
                                               loadingVisible = true;
                                               updatingPlugins.Set(dict => new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>(dict)
                                                   { [id] = (title, icon, 0) });
                                           }, TaskContinuationOptions.OnlyOnRanToCompletion);
                                           #pragma warning restore CS4014

                                           try
                                           {
                                               await updateService.UpdatePluginAsync(id, progress);
                                               updatesQuery.Mutator.Invalidate();
                                               client.Toast($"Updated '{title}' to v{updateInfo.LatestVersion}", "Updated");
                                           }
                                           catch (Exception ex)
                                           {
                                               client.Toast($"Failed to update: {ex.Message}", "Error");
                                           }
                                           finally
                                           {
                                               showLoadingCts.Cancel();
                                               showLoadingCts.Dispose();
                                               updatingPlugins.Set(dict =>
                                               {
                                                   var next = new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>(dict);
                                                   next.Remove(id);
                                                   return next;
                                               });
                                           }
                                       }, variant: ButtonVariant.Primary, icon: Icons.Download).Small())
                                   : null!)
                           | (updateInfo != null ? new Separator() : null!)
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
                               }, variant: ButtonVariant.Outline, icon: Icons.Power)
                               | BuildUninstallButton(id, manifest?.Title, pluginDirectories.GetValueOrDefault(id), uninstallService, client))
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
                       var uninstallButton = BuildUninstallButton(p.Id, manifest?.Title, p.Directory, uninstallService, client);
                       var header = Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween)
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                               | PluginIconHelper.ToWidget(manifest?.Icon)
                               | Text.Block(p.Title))
                           | (Layout.Horizontal().Gap(1).AlignContent(Align.Right)
                               | Text.Block("Unconfigured").Muted().Small()
                               | new Icon(Icons.TriangleAlert, Colors.Warning));
                       var content = Layout.Vertical().Gap(3)
                           | Text.Block(string.Join(", ", p.ValidationErrors)).Muted().Small()
                           | (customView ?? new PluginConfigurationView(p.Id, p.Schema, configFactory) { ExtraActions = uninstallButton }.Key(p.Id));
                       return (object)new Expandable(header, content) { Key = p.Id };
                   })).ToArray())
               | (unloadedPlugins.Count == 0 ? null! :
                   (object)(Layout.Vertical().Gap(4)
                   | new Separator()
                   | Text.Block("Unloaded Plugins").Bold()
                   | unloadedPlugins.Select(p =>
                   {
                       var isFailed = p.FailureReason is not null;
                       var displayName = p.Title ?? p.Id;
                       var header = Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween)
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                               | (PluginIconHelper.ToWidget(p.Icon) ?? PluginIconHelper.UnloadedIcon())
                               | Text.Block(displayName))
                           | (isFailed
                               ? (Layout.Horizontal().Gap(1).AlignContent(Align.Right)
                                   | Text.Block("Failed").Muted().Small()
                                   | new Icon(Icons.TriangleAlert, Colors.Destructive))
                               : null!);
                       var content = Layout.Vertical().Gap(2)
                           | (isFailed ? (object)new Callout(p.FailureReason!).Variant(CalloutVariant.Destructive) : null!)
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                               | new Button(isFailed ? "Retry" : "Load", onClick: _ =>
                               {
                                   var success = pluginManager.LoadPlugin(p.Directory);
                                   client.Toast(success ? $"Loaded '{displayName}'" : $"Failed to load '{displayName}'",
                                       success ? "Installed" : "Error");
                                   return ValueTask.CompletedTask;
                               }, variant: ButtonVariant.Outline, icon: isFailed ? Icons.RefreshCw : Icons.Plus)
                               | BuildUninstallButton(p.Id, p.Title, p.Directory, uninstallService, client));
                       return (object)new Expandable(header, content).Open();
                   }).ToArray()))
               | (installingPlugins.Value.Count > 0
                   ? (object)(Layout.Vertical().Gap(2)
                       | new Separator()
                       | Text.Block("Installing").Bold()
                       | installingPlugins.Value.Select(kvp =>
                           (object)(Layout.Vertical().Gap(2)
                               | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                                   | (PluginIconHelper.ToWidget(kvp.Value.Icon)
                                       ?? (object)new Icon(Icons.Plug).Width(PluginIconHelper.IconSize).Height(PluginIconHelper.IconSize))
                                   | Text.Block(kvp.Value.Title))
                               | new Progress(kvp.Value.Progress))
                       ).ToArray())
                   : null!)
               | new Separator()
               | (Layout.Horizontal().Gap(2)
                   | new AddPluginsDialogView(availableQuery.Loading, availablePlugins, pluginsDir, client, dependencyResolver, installingPlugins, pluginManager, httpClientFactory)
                   | (updatesQuery.Value?.Any(u => u.HasUpdate) == true && updatingPlugins.Value.Count == 0
                       ? new Button("Update All", onClick: async _ =>
                       {
                           try
                           {
                               await updateService.UpdateAllAsync();
                               updatesQuery.Mutator.Invalidate();
                               client.Toast("All plugins updated", "Updated");
                           }
                           catch (Exception ex)
                           {
                               client.Toast($"Some updates failed: {ex.Message}", "Error");
                           }
                       }, variant: ButtonVariant.Outline, icon: Icons.CircleArrowUp)
                       : null!)
                   | new Button("Open Plugins Folder", onClick: _ =>
                   {
                       PlatformHelper.OpenInFileManager(pluginsDir);
                       return ValueTask.CompletedTask;
                   }, variant: ButtonVariant.Outline, icon: Icons.FolderOpen));
    }

    private class AddPluginsDialogView(
        bool loading, AvailablePlugin[]? plugins, string pluginsDir, IClientProvider client,
        NuGetDependencyResolver dependencyResolver, IState<Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>> installingPlugins,
        IPluginManager pluginManager, IHttpClientFactory httpClientFactory, ButtonVariant buttonVariant = ButtonVariant.Outline) : ViewBase
    {
        public override object Build()
        {
            var isOpen = UseState(false);
            var searchQuery = UseState("");
            var nugetDialogOpen = UseState(false);
            var nugetId = UseState("");
            var nugetInstalling = UseState(false);
            var localDialogOpen = UseState(false);
            var localPath = UseState("");
            Context.TryUseService<DesktopWindow>(out var desktop);

            var filtered = plugins?
                .Where(p =>
                {
                    if (string.IsNullOrWhiteSpace(searchQuery.Value)) return true;
                    var query = searchQuery.Value.Trim();
                    return p.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (p.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
                })
                .ToArray();

            object? dialogContent;
            if (loading)
            {
                dialogContent = Layout.Vertical().Gap(2).AlignContent(Align.Center)
                    .Height(Size.Rem(12)).Width(Size.Full())
                    | new Loading()
                    | Text.Muted("Loading available plugins...");
            }
            else if (filtered == null || filtered.Length == 0)
            {
                var message = plugins is { Length: > 0 }
                    ? "No plugins match your search."
                    : "No plugins available to install.";
                dialogContent = Layout.Vertical().AlignContent(Align.Center)
                    .Height(Size.Rem(8)).Width(Size.Full())
                    | Text.Muted(message);
            }
            else
            {
                dialogContent = Layout.Vertical().Scroll(Scroll.Auto)
                    .Height(Size.Rem(20)).Width(Size.Full()).Gap(2)
                    | filtered.Select(p =>
                    {
                        var icon = PluginIconHelper.FromApiResponse(p.IconKind, p.IconValue, p.IconUrl);
                        var isInstalling = installingPlugins.Value.ContainsKey(p.PackageId);
                        var progressValue = isInstalling ? installingPlugins.Value[p.PackageId].Progress : 0;

                        return (object)(Layout.Horizontal().Gap(2).AlignContent(Align.SpaceBetween).Width(Size.Full())
                            | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                                | (PluginIconHelper.ToWidget(icon)
                                    ?? (object)new Icon(Icons.Plug).Width(PluginIconHelper.IconSize).Height(PluginIconHelper.IconSize))
                                | (Layout.Vertical().Gap(0)
                                    | (Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                                        | Text.Block(p.Title)
                                        | new Badge(p.Version, BadgeVariant.Secondary))
                                    | (p.Description is not null
                                        ? (object)Text.Block(p.Description).Muted().Small()
                                        : null!)))
                            | (isInstalling
                                ? (object)new Progress(progressValue).Width(Size.Units(20))
                                : new Button("Install", onClick: async _ =>
                                {
                                    // Delay showing loading state to avoid a flash for fast installs
                                    var showLoadingCts = new CancellationTokenSource();
                                    var loadingVisible = false;

                                    var progress = new Progress<int>(pct =>
                                    {
                                        if (!loadingVisible) return;
                                        installingPlugins.Set(dict => new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>(dict)
                                            { [p.PackageId] = (p.Title, icon, pct) });
                                    });

                                    #pragma warning disable CS4014 // Fire-and-forget by design
                                    Task.Delay(400, showLoadingCts.Token).ContinueWith(__ =>
                                    {
                                        loadingVisible = true;
                                        installingPlugins.Set(dict => new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>(dict)
                                            { [p.PackageId] = (p.Title, icon, 0) });
                                    }, TaskContinuationOptions.OnlyOnRanToCompletion);
                                    #pragma warning restore CS4014

                                    try
                                    {
                                        await InstallPluginAsync(p, pluginsDir, dependencyResolver, progress);
                                        pluginManager.LoadPlugin(Path.Combine(pluginsDir, p.PackageId));
                                        client.Toast($"Installed '{p.Title}'", "Installed");
                                    }
                                    catch (Exception ex)
                                    {
                                        client.Toast($"Failed to install: {ex.Message}", "Error");
                                    }
                                    finally
                                    {
                                        showLoadingCts.Cancel();
                                        showLoadingCts.Dispose();
                                        installingPlugins.Set(dict =>
                                        {
                                            var next = new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>(dict);
                                            next.Remove(p.PackageId);
                                            return next;
                                        });
                                    }
                                }, variant: ButtonVariant.Outline, icon: Icons.Download)));
                    }).ToArray();
            }

            // Build menu items for the "Add Unapproved Plugin" dropdown
            var localPluginLabel = desktop != null ? "Browse for local plugin..." : "Add local plugin...";
            var addUnapprovedButton = new Button("Add Unapproved Plugin", icon: Icons.Plus, variant: ButtonVariant.Outline)
                .WithDropDown(
                    new MenuItem(localPluginLabel, Icon: Icons.FolderOpen, Tag: "browse"),
                    new MenuItem("Add NuGet package...", Icon: Icons.Package, Tag: "nuget"))
                .OnSelect(evt =>
                {
                    switch (evt.Value)
                    {
                        case "browse":
                            if (desktop != null)
                            {
                                var picked = desktop.ShowSelectFolderDialog("Select plugin folder");
                                if (picked is { Length: > 0 } && !string.IsNullOrEmpty(picked[0]))
                                {
                                    AddLocalPluginReference(picked[0], pluginsDir, client);
                                }
                            }
                            else
                            {
                                localDialogOpen.Set(true);
                            }
                            break;
                        case "nuget":
                            nugetDialogOpen.Set(true);
                            break;
                    }
                });

            return new Fragment(
                new Button("Add Plugins", _ =>
                {
                    isOpen.Value = true;
                    return ValueTask.CompletedTask;
                }, variant: buttonVariant, icon: Icons.Plus),
                isOpen.Value ? new Dialog(
                    _ => { isOpen.Set(false); searchQuery.Set(""); },
                    new DialogHeader("Add Plugins"),
                    new DialogBody(
                        Layout.Vertical().Gap(3)
                        | searchQuery.ToTextInput().Placeholder("Search plugins...")
                        | dialogContent
                    ),
                    new DialogFooter(addUnapprovedButton)
                ).Width(Size.Rem(40)) : null,
                // Add NuGet Package dialog
                nugetDialogOpen.Value ? new Dialog(
                    _ => { nugetDialogOpen.Set(false); nugetId.Set(""); },
                    new DialogHeader("Add NuGet Package"),
                    new DialogBody(
                        Layout.Vertical().Gap(3)
                        | Text.Muted("Enter a NuGet package ID to install directly from nuget.org.")
                        | nugetId.ToTextInput().Placeholder("Package ID (e.g. MyCompany.Plugin)")
                            .OnSubmit(() => { _ = InstallNuGetByIdAsync(nugetId, nugetInstalling, nugetDialogOpen, client, pluginsDir, dependencyResolver, installingPlugins, pluginManager, httpClientFactory); })
                    ),
                    new DialogFooter(
                        new Button("Cancel", _ => { nugetDialogOpen.Set(false); nugetId.Set(""); }, variant: ButtonVariant.Outline),
                        new Button("Install", onClick: async _ =>
                        {
                            await InstallNuGetByIdAsync(nugetId, nugetInstalling, nugetDialogOpen, client, pluginsDir, dependencyResolver, installingPlugins, pluginManager, httpClientFactory);
                        }, variant: ButtonVariant.Primary, icon: Icons.Download)
                            .Disabled(string.IsNullOrWhiteSpace(nugetId.Value) || nugetInstalling.Value)
                            .Loading(nugetInstalling.Value))
                ).Width(Size.Rem(30)) : null,
                // Add Local Plugin dialog (web fallback when no desktop picker)
                localDialogOpen.Value ? new Dialog(
                    _ => { localDialogOpen.Set(false); localPath.Set(""); },
                    new DialogHeader("Add Local Plugin"),
                    new DialogBody(
                        Layout.Vertical().Gap(3)
                        | Text.Muted("Enter the absolute path to a local plugin folder.")
                        | localPath.ToTextInput().Placeholder("/path/to/plugin/folder")
                            .OnSubmit(() =>
                            {
                                if (!string.IsNullOrWhiteSpace(localPath.Value))
                                {
                                    AddLocalPluginReference(localPath.Value.Trim(), pluginsDir, client);
                                    localPath.Set("");
                                    localDialogOpen.Set(false);
                                }
                            })
                    ),
                    new DialogFooter(
                        new Button("Cancel", _ => { localDialogOpen.Set(false); localPath.Set(""); }, variant: ButtonVariant.Outline),
                        new Button("Add", _ =>
                        {
                            if (!string.IsNullOrWhiteSpace(localPath.Value))
                            {
                                AddLocalPluginReference(localPath.Value.Trim(), pluginsDir, client);
                                localPath.Set("");
                                localDialogOpen.Set(false);
                            }
                            return ValueTask.CompletedTask;
                        }, variant: ButtonVariant.Primary, icon: Icons.FolderPlus)
                            .Disabled(string.IsNullOrWhiteSpace(localPath.Value)))
                ).Width(Size.Rem(30)) : null
            );
        }
    }

    private static async Task InstallPluginAsync(AvailablePlugin plugin, string pluginsDir,
        NuGetDependencyResolver dependencyResolver, IProgress<int>? progress = null)
    {
        var pluginDir = Path.Combine(pluginsDir, plugin.PackageId);

        // Extract and resolve in a temp directory to avoid premature loading by PluginWatcher
        var tempDir = Path.Combine(Path.GetTempPath(), "ivy-plugin-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var http = new HttpClient();
            var packageId = plugin.PackageId.ToLowerInvariant();
            var version = plugin.Version.ToLowerInvariant();
            var nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageId}/{version}/{packageId}.{version}.nupkg";

            // Download nupkg (0-10%)
            progress?.Report(0);
            var nupkgBytes = await http.GetByteArrayAsync(nupkgUrl);
            progress?.Report(10);

            // Extract nupkg to temp dir (10-20%)
            using var archive = new ZipArchive(new MemoryStream(nupkgBytes));
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var destPath = Path.GetFullPath(Path.Combine(tempDir, entry.FullName));
                if (!destPath.StartsWith(tempDir + Path.DirectorySeparatorChar))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                await using var entryStream = entry.Open();
                await using var fileStream = File.Create(destPath);
                await entryStream.CopyToAsync(fileStream);
            }
            progress?.Report(20);

            // Resolve and download transitive dependencies (20-100%)
            var depProgress = new Progress<int>(p => progress?.Report(20 + (int)(p / 100.0 * 80)));
            await dependencyResolver.ResolveAndInstallDependenciesAsync(tempDir, plugin.PackageId, plugin.Version, depProgress);

            // Move complete plugin directory into plugins/ (atomic on same filesystem)
            if (Directory.Exists(pluginDir))
                Directory.Delete(pluginDir, recursive: true);
            Directory.Move(tempDir, pluginDir);
            progress?.Report(100);
        }
        catch
        {
            // Clean up temp on failure
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
            throw;
        }
    }

    private static void AddLocalPluginReference(string selectedPath, string pluginsDir, IClientProvider client)
    {
        try
        {
            var refsPath = Path.Combine(pluginsDir, PluginReferencesWatcher.FileName);
            var lines = File.Exists(refsPath)
                ? File.ReadAllLines(refsPath).ToList()
                : [];

            // Add as a YAML list entry
            lines.Add($"- {selectedPath}");
            Directory.CreateDirectory(pluginsDir);
            File.WriteAllLines(refsPath, lines);

            client.Toast($"Added local plugin reference: {Path.GetFileName(selectedPath)}", "Plugin Added");
        }
        catch (Exception ex)
        {
            client.Toast($"Failed to add plugin reference: {ex.Message}", "Error");
        }
    }

    private static async Task InstallNuGetByIdAsync(
        IState<string> nugetId, IState<bool> nugetInstalling, IState<bool> dialogOpen, IClientProvider client,
        string pluginsDir, NuGetDependencyResolver dependencyResolver,
        IState<Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>> installingPlugins,
        IPluginManager pluginManager, IHttpClientFactory httpClientFactory)
    {
        var packageId = nugetId.Value?.Trim();
        if (string.IsNullOrWhiteSpace(packageId)) return;

        nugetInstalling.Set(true);
        try
        {
            // Fetch latest version from NuGet flat-container index
            using var http = httpClientFactory.CreateClient();
            var indexUrl = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
            var response = await http.GetAsync(indexUrl);
            if (!response.IsSuccessStatusCode)
            {
                client.Toast($"Package '{packageId}' not found on NuGet.", "Error");
                return;
            }

            var indexJson = await response.Content.ReadFromJsonAsync<JsonElement>();
            var versions = indexJson.GetProperty("versions");
            var latestVersion = versions[versions.GetArrayLength() - 1].GetString()!;

            var plugin = new AvailablePlugin(packageId, latestVersion, "", packageId, null, null, null, null, null);

            installingPlugins.Set(dict => new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>(dict)
                { [packageId] = (packageId, null, 0) });

            var progress = new Progress<int>(pct =>
            {
                installingPlugins.Set(dict => new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>(dict)
                    { [packageId] = (packageId, null, pct) });
            });

            await InstallPluginAsync(plugin, pluginsDir, dependencyResolver, progress);
            pluginManager.LoadPlugin(Path.Combine(pluginsDir, packageId));
            client.Toast($"Installed '{packageId}' v{latestVersion}", "Installed");
            nugetId.Set("");
            dialogOpen.Set(false);
        }
        catch (Exception ex)
        {
            client.Toast($"Failed to install '{packageId}': {ex.Message}", "Error");
        }
        finally
        {
            nugetInstalling.Set(false);
            installingPlugins.Set(dict =>
            {
                var next = new Dictionary<string, (string Title, PluginIcon? Icon, int Progress)>(dict);
                next.Remove(packageId!);
                return next;
            });
        }
    }

    private static object? BuildUninstallButton(
        string pluginId, string? pluginTitle, string? pluginDirectory, PluginUninstallService uninstallService, IClientProvider client)
    {
        if (pluginDirectory is null)
            return null;

        var installationType = uninstallService.GetInstallationType(pluginDirectory);
        if (installationType == PluginInstallationType.Unknown)
            return null;

        return new UninstallConfirmView(pluginId, pluginTitle ?? pluginId, pluginDirectory, installationType, uninstallService, client);
    }

    private class UninstallConfirmView(
        string pluginId, string pluginTitle, string pluginDirectory, PluginInstallationType installationType,
        PluginUninstallService uninstallService, IClientProvider client) : ViewBase
    {
        public override object Build()
        {
            var isOpen = UseState(false);
            var keepConfig = UseState(true);

            var confirmMessage = installationType switch
            {
                PluginInstallationType.NuGet =>
                    "This will permanently delete the plugin package from disk.",
                PluginInstallationType.Referenced =>
                    "This will remove this plugin from your references list. The plugin files will not be deleted.",
                _ => "This will uninstall the plugin."
            };

            return new Fragment(
                new Button("Uninstall", _ =>
                {
                    isOpen.Value = true;
                    return ValueTask.CompletedTask;
                }, variant: ButtonVariant.Outline, icon: Icons.Trash2),
                isOpen.Value ? new Dialog(
                    _ => { isOpen.Set(false); keepConfig.Set(true); },
                    new DialogHeader($"Uninstall {pluginTitle}"),
                    new DialogBody(
                        Layout.Vertical().Gap(3)
                        | confirmMessage
                        | keepConfig.ToBoolInput("Keep plugin configuration")
                    ),
                    new DialogFooter(
                        new Button("Cancel", _ => { isOpen.Value = false; keepConfig.Value = true; }, variant: ButtonVariant.Outline),
                        new Button("Uninstall", _ =>
                        {
                            try
                            {
                                if (installationType == PluginInstallationType.NuGet)
                                    uninstallService.UninstallNuGetPlugin(pluginDirectory);
                                else
                                    uninstallService.UninstallReferencedPlugin(pluginDirectory);

                                if (!keepConfig.Value)
                                    uninstallService.CleanupPluginConfig(pluginId);

                                client.Toast($"Uninstalled '{pluginTitle}'", "Uninstalled");
                            }
                            catch (Exception ex)
                            {
                                client.Toast($"Failed to uninstall: {ex.Message}", "Error");
                            }
                            isOpen.Value = false;
                            keepConfig.Value = true;
                            return ValueTask.CompletedTask;
                        }, variant: ButtonVariant.Destructive)
                    )
                ) : null
            );
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Settings.Dialogs;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Apps.Settings;

public class VaultSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var vaultService = UseService<IVaultService>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();

        var openCreateDialog = UseState(false);
        var openConnectDialog = UseState(false);
        var openPushDialog = UseState(false);
        var openImportDialog = UseState(false);
        var selectedImportItem = UseState<VaultCatalogItem?>(null);
        var selectedPushProject = UseState<string?>(null);
        var isSyncing = UseState(false);

        var autoSyncState = UseState(() => config.Settings.Vault?.AlwaysUpToDate ?? false);

        var statusQuery = UseQuery<VaultStatus, string>(
            "vault_status",
            async (_, _) => await vaultService.GetStatusAsync());

        var catalogQuery = UseQuery<VaultCatalog, string>(
            "vault_catalog",
            async (_, _) => await vaultService.GetCatalogAsync());

        UseEffect(() =>
        {
            void OnVaultChanged()
            {
                refreshToken.Refresh();
                statusQuery.Mutator.Revalidate();
                catalogQuery.Mutator.Revalidate();
                autoSyncState.Set(config.Settings.Vault?.AlwaysUpToDate ?? false);
            }
            vaultService.VaultChanged += OnVaultChanged;
            return Disposable.Create(() => vaultService.VaultChanged -= OnVaultChanged);
        });

        UseEffect(() =>
        {
            _ = vaultService.SetAlwaysUpToDateAsync(autoSyncState.Value);
        }, autoSyncState);

        _ = refreshToken.Token;

        var status = statusQuery.Value ?? new VaultStatus();
        var catalog = catalogQuery.Value ?? new VaultCatalog();

        var localProjectNames = config.Settings.Projects.Select(p => p.Name).ToList();

        var createDialog = new CreateVaultDialog(
            openCreateDialog, vaultService, client,
            onCreated: () => { statusQuery.Mutator.Revalidate(); catalogQuery.Mutator.Revalidate(); });

        var connectDialog = new ConnectVaultDialog(
            openConnectDialog, vaultService, client,
            onConnected: () => { statusQuery.Mutator.Revalidate(); catalogQuery.Mutator.Revalidate(); });

        var pushDialog = new PushToVaultDialog(
            openPushDialog, localProjectNames, selectedPushProject.Value, vaultService, client,
            onPushed: () => { statusQuery.Mutator.Revalidate(); catalogQuery.Mutator.Revalidate(); });

        var importDialog = new ImportFromVaultDialog(
            openImportDialog, selectedImportItem.Value, vaultService, client,
            onImported: () => { statusQuery.Mutator.Revalidate(); catalogQuery.Mutator.Revalidate(); });

        if (!status.IsConfigured)
        {
            var notConfiguredLayout = Layout.Vertical().Width(Size.Auto().Max(Size.Units(200)))
                | Text.Block("Team Configuration Vault").Bold()
                | Text.Block("Share and synchronize Tendril projects, custom skills, MCP servers, and security rules across your team via a versioned Git repository.").Muted().Small()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | new Button("Create GitHub Vault")
                        .Icon(Icons.Plus)
                        .Primary()
                        .OnClick(() => openCreateDialog.Set(true))
                    | new Button("Connect Existing Git Vault")
                        .Icon(Icons.GitBranch)
                        .Outline()
                        .OnClick(() => openConnectDialog.Set(true)));

            return new Fragment(notConfiguredLayout, createDialog, connectDialog);
        }

        async Task HandleSync()
        {
            if (isSyncing.Value) return;
            isSyncing.Set(true);
            var result = await vaultService.PullLatestAsync();
            isSyncing.Set(false);

            if (result.Success)
            {
                client.Toast(result.Message, "Vault Synchronized");
                statusQuery.Mutator.Revalidate();
                catalogQuery.Mutator.Revalidate();
            }
            else
            {
                client.Toast(result.ErrorMessage ?? result.Message, "Sync Failed").Destructive();
            }
        }

        var connectionSection = Layout.Vertical()
            | Text.Block("Vault Connection").Bold()
            | (Layout.Horizontal().AlignContent(Align.SpaceBetween)
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | Icons.FolderGit2.ToIcon()
                    | Text.Monospaced(status.RepoUrl).Bold()
                    | new Badge(status.CurrentBranch).Variant(BadgeVariant.Secondary).Small())
                | (Layout.Horizontal().AlignContent(Align.Right)
                    | new Button("Sync / Pull Latest")
                        .Icon(Icons.RefreshCw)
                        .Outline()
                        .Small()
                        .Loading(isSyncing.Value)
                        .OnClick(async () => await HandleSync())
                    | new Button("Publish Update (PR)")
                        .Icon(Icons.GitPullRequest)
                        .Primary()
                        .Small()
                        .OnClick(() =>
                        {
                            selectedPushProject.Set(null);
                            openPushDialog.Set(true);
                        })))
            | (status.LastSyncedAt.HasValue && status.LastSyncedAt.Value.Year > 1
                ? Text.Muted($"Last synced: {status.LastSyncedAt.Value:MMM d, yyyy HH:mm} UTC").Small()
                : null)
            | autoSyncState.ToBoolInput("Always keep local configuration in sync with remote");

        var tableRows = catalog.Projects.Select((p, i) =>
        {
            var parts = new List<string>();
            if (p.ReposCount > 0) parts.Add($"{p.ReposCount} repos");
            if (p.SkillsCount > 0) parts.Add($"{p.SkillsCount} skills");
            if (p.McpsCount > 0) parts.Add($"{p.McpsCount} MCPs");
            if (p.MemoriesCount > 0) parts.Add($"{p.MemoriesCount} memories");
            if (p.ReviewActionsCount > 0) parts.Add($"{p.ReviewActionsCount} actions");
            if (p.VerificationsCount > 0) parts.Add($"{p.VerificationsCount} verifs");
            var contentsStr = parts.Count > 0 ? string.Join(" • ", parts) : $"{p.ReposCount} repos";

            return new VaultProjectTableRow(
                p.Name,
                !string.IsNullOrEmpty(p.RemoteVersion) ? $"v{p.RemoteVersion}" : (!string.IsNullOrEmpty(p.LocalVersion) ? $"v{p.LocalVersion}" : "-"),
                p.SyncStatus,
                contentsStr,
                p.LatestChangelog ?? (!string.IsNullOrEmpty(p.Description) ? p.Description : "-"),
                i
            );
        }).ToList();

        var projectsTable = new TableBuilder<VaultProjectTableRow>(tableRows)
            .Builder(t => t.Name, f => f.Func<VaultProjectTableRow, string>(name =>
            {
                var item = catalog.Projects.Find(p => p.Name == name);
                var colorStr = item?.Color ?? "Slate";
                var color = Enum.TryParse<Colors>(colorStr, out var c) ? c : Colors.Slate;
                return Layout.Horizontal().AlignContent(Align.Left)
                    | new Badge("").Color(color).Small()
                    | Text.Block(name).Bold();
            }))
            .Builder(t => t.Version, f => f.Func<VaultProjectTableRow, string>(v =>
                new Badge(v).Variant(BadgeVariant.Secondary).Small()
            ))
            .Builder(t => t.SyncStatus, f => f.Func<VaultProjectTableRow, VaultItemSyncStatus>(s => s switch
            {
                VaultItemSyncStatus.UpToDate => new Badge("✓ In Sync").Variant(BadgeVariant.Secondary).Small(),
                VaultItemSyncStatus.UpdateAvailable => new Badge("Update Available").Variant(BadgeVariant.Destructive).Small(),
                VaultItemSyncStatus.LocalOnly => new Badge("Local Only").Variant(BadgeVariant.Outline).Small(),
                VaultItemSyncStatus.NotImported => new Badge("Not Imported").Variant(BadgeVariant.Outline).Small(),
                _ => new Badge("In Vault").Variant(BadgeVariant.Secondary).Small()
            }))
            .Header(t => t.Contents, "Contents")
            .Header(t => t.Changelog, "Changelog / Context")
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<VaultProjectTableRow, int>(idx =>
            {
                var item = catalog.Projects[idx];
                return item.SyncStatus switch
                {
                    VaultItemSyncStatus.NotImported => new Button("Import")
                        .Icon(Icons.Download)
                        .Primary()
                        .Small()
                        .OnClick(() =>
                        {
                            selectedImportItem.Set(item);
                            openImportDialog.Set(true);
                        }),

                    VaultItemSyncStatus.UpdateAvailable => new Button("Update")
                        .Icon(Icons.CircleArrowUp)
                        .Primary()
                        .Small()
                        .OnClick(async () =>
                        {
                            var tracking = config.Settings.Vault?.TrackedProjects.TryGetValue(item.Name, out var t) == true ? t : null;
                            var mappings = tracking?.LocalRepoPaths ?? new();
                            var res = await vaultService.ImportProjectAsync(item.Name, mappings);
                            if (res.Success)
                            {
                                client.Toast(res.Message, "Updated");
                                catalogQuery.Mutator.Revalidate();
                            }
                        }),

                    VaultItemSyncStatus.LocalOnly => new Button("Publish")
                        .Icon(Icons.Upload)
                        .Outline()
                        .Small()
                        .OnClick(() =>
                        {
                            selectedPushProject.Set(item.Name);
                            openPushDialog.Set(true);
                        }),

                    VaultItemSyncStatus.UpToDate => new Button("Publish PR")
                        .Icon(Icons.GitPullRequest)
                        .Outline()
                        .Small()
                        .OnClick(() =>
                        {
                            selectedPushProject.Set(item.Name);
                            openPushDialog.Set(true);
                        }),

                    _ => null
                };
            }))
            .Width(Size.Fit());

        var projectsSection = Layout.Vertical()
            | Text.Block("Shared Projects").Bold()
            | Text.Block("Projects tracked in the team vault and their local sync status.").Muted().Small()
            | (tableRows.Count > 0
                ? projectsTable
                : Text.Muted("No projects found in the vault yet. Click 'Publish Update (PR)' above to share your local projects.").Small());

        var disconnectSection = Layout.Vertical()
            | Text.Block("Danger Zone").Bold()
            | Text.Block("Disconnect this Tendril instance from the shared team vault.").Muted().Small()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Button("Disconnect Vault").Destructive().Outline().OnClick(async () =>
                {
                    await vaultService.DisconnectVaultAsync();
                    statusQuery.Mutator.Revalidate();
                    catalogQuery.Mutator.Revalidate();
                }));

        var mainLayout = Layout.Vertical().Width(Size.Auto().Max(Size.Units(200)))
            | Text.Block("Team Configuration Vault").Bold()
            | Text.Block("Share and synchronize Tendril projects, custom skills, MCP servers, and security rules across your team.").Muted().Small()
            | connectionSection
            | projectsSection
            | disconnectSection;

        return new Fragment(
            mainLayout,
            createDialog,
            connectDialog,
            pushDialog,
            importDialog
        );
    }

    private record VaultProjectTableRow(
        string Name,
        string Version,
        VaultItemSyncStatus SyncStatus,
        string Contents,
        string Changelog,
        int Index
    );
}

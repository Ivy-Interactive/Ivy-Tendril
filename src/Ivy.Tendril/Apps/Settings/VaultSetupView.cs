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
            var notConfiguredLayout = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
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

        var headerCard = Layout.Vertical()
            | (Layout.Horizontal()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | Icons.FolderGit2.ToIcon()
                    | Text.Block("Team Vault").Bold()
                    | new Badge(status.CurrentBranch).Variant(BadgeVariant.Secondary))
                | (Layout.Horizontal().AlignContent(Align.Right)
                    | new Button("Sync / Pull Latest")
                        .Icon(Icons.RefreshCw)
                        .Outline()
                        .Loading(isSyncing.Value)
                        .OnClick(async () => await HandleSync())
                    | new Button("Publish Update (PR)")
                        .Icon(Icons.GitPullRequest)
                        .Primary()
                        .OnClick(() =>
                        {
                            selectedPushProject.Set(null);
                            openPushDialog.Set(true);
                        })))
            | (Layout.Horizontal().AlignContent(Align.Left)
                | Text.Block($"Repository: {status.RepoUrl}").Small().Muted()
                | (status.LastSyncedAt.HasValue
                    ? Text.Block($"• Last synced: {status.LastSyncedAt.Value:MMM d, HH:mm} UTC").Small().Muted()
                    : null))
            | autoSyncState.ToBoolInput("Always keep local configuration in sync with remote");

        var projectRows = Layout.Vertical();
        projectRows |= Text.Block("Shared Projects").Bold();

        if (catalog.Projects.Count == 0)
        {
            projectRows |= Text.P("No projects found in the vault yet. Click 'Publish Update' to share your local projects with the team.").Small().Muted();
        }
        else
        {
            foreach (var item in catalog.Projects)
            {
                var rowActions = Layout.Horizontal().AlignContent(Align.Right);

                switch (item.SyncStatus)
                {
                    case VaultItemSyncStatus.NotImported:
                        rowActions |= new Button("Import")
                            .Icon(Icons.Download)
                            .Primary()
                            .Small()
                            .OnClick(() =>
                            {
                                selectedImportItem.Set(item);
                                openImportDialog.Set(true);
                            });
                        break;

                    case VaultItemSyncStatus.UpdateAvailable:
                        rowActions |= new Button("Update")
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
                            });
                        break;

                    case VaultItemSyncStatus.LocalOnly:
                        rowActions |= new Button("Publish")
                            .Icon(Icons.Upload)
                            .Outline()
                            .Small()
                            .OnClick(() =>
                            {
                                selectedPushProject.Set(item.Name);
                                openPushDialog.Set(true);
                            });
                        break;

                    case VaultItemSyncStatus.UpToDate:
                    default:
                        rowActions |= new Badge("✓ In Sync").Variant(BadgeVariant.Secondary);
                        break;
                }

                var statusBadge = item.SyncStatus switch
                {
                    VaultItemSyncStatus.UpToDate => new Badge($"v{item.RemoteVersion}").Variant(BadgeVariant.Secondary),
                    VaultItemSyncStatus.UpdateAvailable => new Badge($"Update: v{item.RemoteVersion}").Variant(BadgeVariant.Destructive),
                    VaultItemSyncStatus.LocalOnly => new Badge("Local Only").Variant(BadgeVariant.Outline),
                    VaultItemSyncStatus.NotImported => new Badge($"v{item.RemoteVersion}").Variant(BadgeVariant.Outline),
                    _ => new Badge("In Vault").Variant(BadgeVariant.Secondary)
                };

                var projectCard = Layout.Horizontal()
                    | (Layout.Vertical()
                        | (Layout.Horizontal().AlignContent(Align.Left)
                            | Text.Block(item.Name).Bold()
                            | statusBadge)
                        | (!string.IsNullOrEmpty(item.Description)
                            ? Text.P(item.Description).Small().Muted()
                            : null)
                        | (!string.IsNullOrEmpty(item.LatestChangelog)
                            ? Text.P($"Changelog: {item.LatestChangelog}").Small().Muted()
                            : null)
                        | (Layout.Horizontal().AlignContent(Align.Left)
                            | Text.Block($"{item.ReposCount} repos").Small().Muted()
                            | Text.Block($"• {item.SkillsCount} skills").Small().Muted()
                            | Text.Block($"• {item.McpsCount} MCPs").Small().Muted()))
                    | rowActions;

                projectRows |= projectCard;
            }
        }

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

        var mainLayout = Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
            | Text.Block("Team Configuration Vault").Bold()
            | Text.Block("Share and synchronize Tendril projects, custom skills, MCP servers, and security rules across your team.").Muted().Small()
            | headerCard
            | projectRows
            | disconnectSection;

        return new Fragment(
            mainLayout,
            createDialog,
            connectDialog,
            pushDialog,
            importDialog
        );
    }
}

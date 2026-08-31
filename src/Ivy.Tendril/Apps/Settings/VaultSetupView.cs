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

        var selectedVaultId = UseState<string>(() =>
        {
            var active = config.Settings.Vaults.FirstOrDefault(v => v.Enabled);
            return active?.Id ?? config.Settings.Vault?.Id ?? "";
        });

        var autoSyncState = UseState(() =>
        {
            var cur = config.Settings.Vaults.FirstOrDefault(v => v.Id == selectedVaultId.Value) ?? config.Settings.Vault;
            return cur?.AlwaysUpToDate ?? false;
        });

        var vaultsQuery = UseQuery<List<VaultStatus>, string>(
            "all",
            async (_, _) => await vaultService.GetVaultsAsync());

        var statusQuery = UseQuery<VaultStatus, string>(
            selectedVaultId.Value,
            async (vaultId, _) => await vaultService.GetStatusAsync(vaultId));

        var catalogQuery = UseQuery<VaultCatalog, string>(
            selectedVaultId.Value,
            async (vaultId, _) => await vaultService.GetCatalogAsync(vaultId));

        var vaultsList = vaultsQuery.Value ?? new List<VaultStatus>();

        UseEffect(() =>
        {
            if ((string.IsNullOrEmpty(selectedVaultId.Value) || !vaultsList.Any(v => v.Id == selectedVaultId.Value)) && vaultsList.Count > 0)
            {
                selectedVaultId.Set(vaultsList[0].Id);
            }
        }, vaultsQuery);

        UseEffect(() =>
        {
            void OnVaultChanged()
            {
                refreshToken.Refresh();
                vaultsQuery.Mutator.Revalidate();
                statusQuery.Mutator.Revalidate();
                catalogQuery.Mutator.Revalidate();
                var cur = config.Settings.Vaults.FirstOrDefault(v => v.Id == selectedVaultId.Value) ?? config.Settings.Vault;
                autoSyncState.Set(cur?.AlwaysUpToDate ?? false);
            }
            vaultService.VaultChanged += OnVaultChanged;
            return Disposable.Create(() => vaultService.VaultChanged -= OnVaultChanged);
        });

        UseEffect(() =>
        {
            var cur = config.Settings.Vaults.FirstOrDefault(v => v.Id == selectedVaultId.Value) ?? config.Settings.Vault;
            if (cur != null && cur.AlwaysUpToDate != autoSyncState.Value)
            {
                _ = vaultService.SetAlwaysUpToDateAsync(autoSyncState.Value, selectedVaultId.Value);
            }
        }, autoSyncState);

        _ = refreshToken.Token;

        var status = statusQuery.Value ?? new VaultStatus();
        var catalog = catalogQuery.Value ?? new VaultCatalog();
        var currentVault = config.Settings.Vaults.FirstOrDefault(v => v.Id == selectedVaultId.Value) ?? config.Settings.Vault;

        var localProjectNames = config.Settings.Projects.Select(p => p.Name).ToList();

        var createDialog = new CreateVaultDialog(
            openCreateDialog, vaultService, client,
            onCreated: () =>
            {
                vaultsQuery.Mutator.Revalidate();
                statusQuery.Mutator.Revalidate();
                catalogQuery.Mutator.Revalidate();
            });

        var connectDialog = new ConnectVaultDialog(
            openConnectDialog, vaultService, client,
            onConnected: () =>
            {
                vaultsQuery.Mutator.Revalidate();
                statusQuery.Mutator.Revalidate();
                catalogQuery.Mutator.Revalidate();
            });

        var pushDialog = new PushToVaultDialog(
            openPushDialog, localProjectNames, selectedPushProject.Value, vaultService, client,
            onPushed: () =>
            {
                vaultsQuery.Mutator.Revalidate();
                statusQuery.Mutator.Revalidate();
                catalogQuery.Mutator.Revalidate();
            },
            initialVaultId: selectedVaultId.Value);

        var importDialog = new ImportFromVaultDialog(
            openImportDialog, selectedImportItem.Value, vaultService, client,
            onImported: () =>
            {
                vaultsQuery.Mutator.Revalidate();
                statusQuery.Mutator.Revalidate();
                catalogQuery.Mutator.Revalidate();
            });

        if (vaultsList.Count == 0 && !status.IsConfigured)
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
            var result = await vaultService.PullLatestAsync(selectedVaultId.Value);
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

        object? vaultSwitcher = null;
        if (vaultsList.Count > 1)
        {
            var vaultOptions = vaultsList
                .Select(v => new Option<string>($"{v.Name} ({v.RepoUrl})", v.Id))
                .ToArray();

            vaultSwitcher = Layout.Horizontal().AlignContent(Align.SpaceBetween)
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | Text.Block("Active Vault:").Bold().Small()
                    | selectedVaultId.ToSelectInput(vaultOptions).Small())
                | (Layout.Horizontal().AlignContent(Align.Right)
                    | new Button("Connect Another Vault")
                        .Icon(Icons.Plus)
                        .Outline()
                        .Small()
                        .OnClick(() => openConnectDialog.Set(true))
                    | new Button("Create Vault")
                        .Icon(Icons.GitBranch)
                        .Outline()
                        .Small()
                        .OnClick(() => openCreateDialog.Set(true)));
        }
        else
        {
            vaultSwitcher = Layout.Horizontal().AlignContent(Align.Right)
                | new Button("Connect Another Vault")
                    .Icon(Icons.Plus)
                    .Outline()
                    .Small()
                    .OnClick(() => openConnectDialog.Set(true))
                | new Button("Create Vault")
                    .Icon(Icons.GitBranch)
                    .Outline()
                    .Small()
                    .OnClick(() => openCreateDialog.Set(true));
        }

        var connectionSection = Layout.Vertical()
            | vaultSwitcher
            | Text.Block("Vault Connection").Bold()
            | (Layout.Horizontal().AlignContent(Align.SpaceBetween)
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | Icons.FolderGit2.ToIcon()
                    | Text.Monospaced(!string.IsNullOrEmpty(status.RepoUrl) ? status.RepoUrl : status.Name).Bold()
                    | new Badge(status.CurrentBranch).Variant(BadgeVariant.Secondary).Small()
                    | (status.CommitsBehind > 0 ? new Badge($"{status.CommitsBehind} behind").Variant(BadgeVariant.Warning).Small() : null)
                    | (status.CommitsAhead > 0 ? new Badge($"{status.CommitsAhead} ahead").Variant(BadgeVariant.Secondary).Small() : null))
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
            return new VaultProjectTableRow(
                p.Name,
                !string.IsNullOrEmpty(p.RemoteVersion) ? $"v{p.RemoteVersion}" : (!string.IsNullOrEmpty(p.LocalVersion) ? $"v{p.LocalVersion}" : "-"),
                p.SyncStatus,
                p,
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
                VaultItemSyncStatus.Conflict => new Badge("Name Conflict").Variant(BadgeVariant.Destructive).Small(),
                _ => new Badge("In Vault").Variant(BadgeVariant.Secondary).Small()
            }))
            .Header(t => t.Item, "Contents")
            .Builder(t => t.Item, f => f.Func<VaultProjectTableRow, VaultCatalogItem>(item =>
            {
                var badges = Layout.Horizontal().AlignContent(Align.Left);
                if (item.ReposCount > 0)
                    badges |= new Badge($"{item.ReposCount} {(item.ReposCount == 1 ? "repo" : "repos")}").Variant(BadgeVariant.Secondary).Small();
                if (item.SkillsCount > 0)
                    badges |= new Badge($"{item.SkillsCount} {(item.SkillsCount == 1 ? "skill" : "skills")}").Variant(BadgeVariant.Secondary).Small();
                if (item.McpsCount > 0)
                    badges |= new Badge($"{item.McpsCount} MCPs").Variant(BadgeVariant.Secondary).Small();
                if (item.MemoriesCount > 0)
                    badges |= new Badge($"{item.MemoriesCount} {(item.MemoriesCount == 1 ? "memory" : "memories")}").Variant(BadgeVariant.Secondary).Small();
                if (item.ReviewActionsCount > 0)
                    badges |= new Badge($"{item.ReviewActionsCount} {(item.ReviewActionsCount == 1 ? "action" : "actions")}").Variant(BadgeVariant.Secondary).Small();
                if (item.VerificationsCount > 0)
                    badges |= new Badge($"{item.VerificationsCount} {(item.VerificationsCount == 1 ? "verif" : "verifs")}").Variant(BadgeVariant.Secondary).Small();

                return badges;
            }))
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

                    VaultItemSyncStatus.Conflict => new Button("Import As...")
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
                            var tracking = currentVault?.TrackedProjects.TryGetValue(item.Name, out var t) == true ? t : null;
                            var mappings = tracking?.LocalRepoPaths ?? new();
                            var res = await vaultService.ImportProjectAsync(item.Name, mappings, selectedVaultId.Value);
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
            | Text.Block($"Disconnect this Tendril instance from '{status.Name}' ({status.RepoUrl}).").Muted().Small()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Button("Disconnect Vault").Destructive().Outline().OnClick(async () =>
                {
                    await vaultService.DisconnectVaultAsync(selectedVaultId.Value);
                    vaultsQuery.Mutator.Revalidate();
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
        VaultCatalogItem Item,
        string Changelog,
        int Index
    );
}

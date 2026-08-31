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
    public override object? Build()
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
        var projectToDelete = UseState<string?>(null);
        var openDeleteConfirm = UseState(false);
        var isDeleting = UseState(false);

        var selectedVaultId = UseState(() =>
        {
            var cur = config.Settings.Vaults.FirstOrDefault(v => v.Enabled) ?? config.Settings.Vault;
            return cur?.Id ?? "";
        });

        var autoSyncState = UseState(() =>
        {
            var cur = config.Settings.Vaults.FirstOrDefault(v => v.Enabled) ?? config.Settings.Vault;
            return cur?.AlwaysUpToDate ?? false;
        });

        var vaultsQuery = UseQuery<List<VaultStatus>, string>(
            "vaults_list",
            async (_, _) => await vaultService.GetVaultsAsync());

        var statusQuery = UseQuery<VaultStatus, string>(
            $"vault_status_{selectedVaultId.Value}",
            async (_, _) => await vaultService.GetStatusAsync(selectedVaultId.Value));

        var catalogQuery = UseQuery<VaultCatalog, string>(
            $"vault_catalog_{selectedVaultId.Value}",
            async (_, _) => await vaultService.GetCatalogAsync(selectedVaultId.Value));

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
        var trackedProjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in config.Settings.Vaults)
        {
            foreach (var k in v.TrackedProjects.Keys)
                trackedProjectNames.Add(k);
        }
        if (config.Settings.Vault != null)
        {
            foreach (var k in config.Settings.Vault.TrackedProjects.Keys)
                trackedProjectNames.Add(k);
        }
        foreach (var cp in catalog.Projects)
        {
            trackedProjectNames.Add(cp.Name);
        }

        var untrackedLocalProjects = config.Settings.Projects
            .Where(p => !trackedProjectNames.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();

        var availablePushProjects = !string.IsNullOrEmpty(selectedPushProject.Value) && !untrackedLocalProjects.Contains(selectedPushProject.Value)
            ? untrackedLocalProjects.Concat(new[] { selectedPushProject.Value }).ToList()
            : untrackedLocalProjects;

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
            openPushDialog, availablePushProjects, selectedPushProject.Value, vaultService, client,
            onPushed: () =>
            {
                vaultsQuery.Mutator.Revalidate();
                statusQuery.Mutator.Revalidate();
                catalogQuery.Mutator.Revalidate();
            },
            initialVaultId: selectedVaultId.Value);

        var importDialog = new ImportFromVaultDialog(
            openImportDialog, selectedImportItem, vaultService, client,
            onImported: () =>
            {
                vaultsQuery.Mutator.Revalidate();
                statusQuery.Mutator.Revalidate();
                catalogQuery.Mutator.Revalidate();
            });

        var confirmDeleteDialog = (openDeleteConfirm.Value && !string.IsNullOrEmpty(projectToDelete.Value))
            ? new Dialog(
                _ => openDeleteConfirm.Set(false),
                new DialogHeader($"Delete '{projectToDelete.Value}' from Vault?"),
                new DialogBody(Layout.Vertical()
                    | Callout.Destructive($"This will remove project '{projectToDelete.Value}' and all its associated manifests, skills, MCP configs, and memories from the vault repository.")
                    | Text.Block("This action commits and pushes the deletion to the vault repository.")),
                new DialogFooter(
                    new Button("Cancel").Outline().OnClick(() => openDeleteConfirm.Set(false)),
                    new Button("Delete from Vault")
                        .Icon(Icons.Trash2)
                        .Destructive()
                        .Loading(isDeleting.Value)
                        .OnClick(async () =>
                        {
                            isDeleting.Set(true);
                            var res = await vaultService.DeleteProjectFromVaultAsync(projectToDelete.Value!, selectedVaultId.Value);
                            isDeleting.Set(false);
                            openDeleteConfirm.Set(false);
                            if (res.Success)
                            {
                                client.Toast(res.Message, "Project Deleted");
                                catalogQuery.Mutator.Revalidate();
                                statusQuery.Mutator.Revalidate();
                            }
                            else
                            {
                                client.Toast(res.ErrorMessage ?? res.Message, "Delete Failed").Destructive();
                            }
                        })
                )
            )
            : null;

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

        var headerToolbar = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Sync")
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
                })
            | new Button("Connect Vault")
                .Icon(Icons.Plus)
                .Outline()
                .Small()
                .OnClick(() => openConnectDialog.Set(true))
            | new Button("Create Vault")
                .Icon(Icons.GitBranch)
                .Outline()
                .Small()
                .OnClick(() => openCreateDialog.Set(true));

        var vaultOptions = vaultsList
            .Select(v => new Option<string>(VaultService.ExtractRepoName(!string.IsNullOrWhiteSpace(v.Name) && v.Name.Contains('/') ? v.Name : v.RepoUrl), v.Id))
            .ToArray();

        var topHeader = Layout.Horizontal().AlignContent(Align.SpaceBetween)
            | (Layout.Horizontal().AlignContent(Align.Left)
                | Text.H2("Team Vault").Bold()
                | (vaultsList.Count > 1
                    ? selectedVaultId.ToSelectInput(vaultOptions).Small()
                    : null))
            | headerToolbar;

        var repoDisplay = !string.IsNullOrEmpty(status.RepoUrl)
            ? status.RepoUrl.Replace("https://github.com/", "").Replace(".git", "")
            : (!string.IsNullOrEmpty(status.Name) ? status.Name : "Team Vault");

        var vaultInfoStrip = Layout.Horizontal().AlignContent(Align.SpaceBetween)
            | (Layout.Horizontal().AlignContent(Align.Left)
                | Icons.FolderGit2.ToIcon()
                | Text.Monospaced(repoDisplay).Bold().Small()
                | (!string.IsNullOrEmpty(status.CurrentBranch) ? new Badge(status.CurrentBranch).Variant(BadgeVariant.Secondary).Small() : null)
                | (status.CommitsBehind > 0 ? new Badge($"{status.CommitsBehind} behind").Variant(BadgeVariant.Warning).Small() : null)
                | (status.CommitsAhead > 0 ? new Badge($"{status.CommitsAhead} ahead").Variant(BadgeVariant.Secondary).Small() : null)
                | (status.LastSyncedAt.HasValue && status.LastSyncedAt.Value.Year > 1
                    ? Text.Muted($"Synced {status.LastSyncedAt.Value:MMM d, HH:mm} UTC").Small()
                    : null))
            | (Layout.Horizontal().AlignContent(Align.Right)
                | autoSyncState.ToBoolInput("Always in sync")
                | new Button("Disconnect").Destructive().Ghost().Small().OnClick(async () =>
                {
                    await vaultService.DisconnectVaultAsync(selectedVaultId.Value);
                    vaultsQuery.Mutator.Revalidate();
                    statusQuery.Mutator.Revalidate();
                    catalogQuery.Mutator.Revalidate();
                }));

        var tableRows = catalog.Projects.Select((p, i) =>
        {
            return new VaultProjectTableRow(
                p.Name,
                !string.IsNullOrEmpty(p.RemoteVersion) ? $"v{p.RemoteVersion}" : (!string.IsNullOrEmpty(p.LocalVersion) ? $"v{p.LocalVersion}" : "-"),
                i,
                p.SyncStatus,
                p,
                p.LatestChangelog ?? (!string.IsNullOrEmpty(p.Description) ? p.Description : "-")
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
            .Header(t => t.Action, "Actions")
            .Builder(t => t.Action, f => f.Func<VaultProjectTableRow, int>(idx =>
            {
                var item = catalog.Projects[idx];
                var actionButtons = Layout.Horizontal().AlignContent(Align.Left);

                switch (item.SyncStatus)
                {
                    case VaultItemSyncStatus.NotImported:
                        actionButtons |= new Button("Import")
                            .Icon(Icons.Download)
                            .Primary()
                            .Small()
                            .OnClick(() =>
                            {
                                selectedImportItem.Set(item);
                                openImportDialog.Set(true);
                            });
                        break;

                    case VaultItemSyncStatus.Conflict:
                        actionButtons |= new Button("Import As...")
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
                        actionButtons |= new Button("Update")
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
                            });
                        break;

                    case VaultItemSyncStatus.LocalOnly:
                        actionButtons |= new Button("Publish")
                            .Icon(Icons.Upload)
                            .Outline()
                            .Small()
                            .OnClick(() =>
                            {
                                selectedPushProject.Set(item.Name);
                                openPushDialog.Set(true);
                            });
                        break;
                }

                actionButtons |= new Button()
                    .Icon(Icons.Trash2)
                    .Destructive()
                    .Ghost()
                    .Small()
                    .Tooltip($"Delete '{item.Name}' from vault")
                    .OnClick(() =>
                    {
                        projectToDelete.Set(item.Name);
                        openDeleteConfirm.Set(true);
                    });

                return actionButtons;
            }))
            .Header(t => t.SyncStatus, "Sync Status")
            .Builder(t => t.SyncStatus, f => f.Func<VaultProjectTableRow, VaultItemSyncStatus>(s => s switch
            {
                VaultItemSyncStatus.UpToDate => new Badge("✓ In Sync").Variant(BadgeVariant.Secondary).Small(),
                VaultItemSyncStatus.UpdateAvailable => new Badge("Update Available").Variant(BadgeVariant.Destructive).Small(),
                VaultItemSyncStatus.LocalOnly => new Badge("Local Only").Variant(BadgeVariant.Outline).Small(),
                VaultItemSyncStatus.NotImported => new Badge("Not Imported").Variant(BadgeVariant.Outline).Small(),
                VaultItemSyncStatus.Conflict => new Badge("Name Conflict").Variant(BadgeVariant.Destructive).Small(),
                _ => new Badge("In Vault").Variant(BadgeVariant.Secondary).Small()
            }))
            .Header(t => t.Contents, "Contents")
            .Builder(t => t.Contents, f => f.Func<VaultProjectTableRow, VaultCatalogItem>(item =>
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
            .Builder(t => t.Changelog, f => f.Func<VaultProjectTableRow, string>(c =>
                Text.Block(c).Small().Muted()
            ))
            .Width(Size.Fit());

        var hasChangelog = catalog.Projects.Any(p =>
            !string.IsNullOrWhiteSpace(p.LatestChangelog) ||
            !string.IsNullOrWhiteSpace(p.Description));

        if (!hasChangelog)
        {
            projectsTable.Remove(t => t.Changelog);
        }

        object projectsSection;
        if (tableRows.Count == 0)
        {
            projectsSection = Layout.Vertical().AlignContent(Align.Left)
                | Text.Block("No Shared Projects").Bold()
                | Text.Block("This vault does not contain any shared projects yet. Add a local project to share it with your team.").Small().Muted()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | new Button("Add Tracked Project")
                        .Icon(Icons.Plus)
                        .Primary()
                        .Small()
                        .OnClick(() =>
                        {
                            selectedPushProject.Set(null);
                            openPushDialog.Set(true);
                        }));
        }
        else
        {
            projectsSection = Layout.Vertical()
                | projectsTable
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | new Button("Add Tracked Project")
                        .Icon(Icons.Plus)
                        .Outline()
                        .Small()
                        .OnClick(() =>
                        {
                            selectedPushProject.Set(null);
                            openPushDialog.Set(true);
                        }));
        }

        var mainLayout = Layout.Vertical().Width(Size.Auto().Max(Size.Units(200)))
            | topHeader
            | vaultInfoStrip
            | projectsSection;

        return new Fragment(
            mainLayout,
            createDialog,
            connectDialog,
            pushDialog,
            importDialog,
            confirmDeleteDialog
        );
    }

    private record VaultProjectTableRow(
        string Name,
        string Version,
        int Action,
        VaultItemSyncStatus SyncStatus,
        VaultCatalogItem Contents,
        string Changelog
    );
}

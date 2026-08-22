using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class ImportRepoPathRow(
    VaultRepoRef repoRef,
    IState<Dictionary<string, string>> repoMappings) : ViewBase
{
    public override object Build()
    {
        var inputState = UseState(() =>
        {
            var key = !string.IsNullOrEmpty(repoRef.Owner) && repoRef.Owner != "local" && repoRef.Owner != "default"
                ? $"{repoRef.Owner}/{repoRef.Name}"
                : repoRef.Name;
            return repoMappings.Value.TryGetValue(key, out var p) ? p : "";
        });

        var repoKey = !string.IsNullOrEmpty(repoRef.Owner) && repoRef.Owner != "local" && repoRef.Owner != "default"
            ? $"{repoRef.Owner}/{repoRef.Name}"
            : repoRef.Name;

        UseEffect(() =>
        {
            if (repoMappings.Value.TryGetValue(repoKey, out var val) && val != inputState.Value)
            {
                inputState.Set(val);
            }
        }, repoMappings);

        UseEffect(() =>
        {
            var updated = new Dictionary<string, string>(repoMappings.Value) { [repoKey] = inputState.Value };
            repoMappings.Set(updated);
        }, inputState);

        var exists = !string.IsNullOrWhiteSpace(inputState.Value) && Directory.Exists(inputState.Value);
        var label = repoRef.RemoteUrl != null
            ? $"{repoKey} ({repoRef.RemoteUrl})"
            : repoKey;

        return Layout.Vertical()
            | inputState.ToTextInput().WithField().Label(label)
            | (!exists && !string.IsNullOrWhiteSpace(repoRef.RemoteUrl)
                ? Text.Block("⚡ Folder not found locally — will auto-clone from remote repository upon import.").Small().Muted()
                : null);
    }
}

public class ImportAssetItemRow(string name, IState<HashSet<string>> selectedSet, string? badge = null) : ViewBase
{
    public override object Build()
    {
        var isChecked = UseState(() => selectedSet.Value.Contains(name));

        UseEffect(() =>
        {
            var contains = selectedSet.Value.Contains(name);
            if (isChecked.Value != contains) isChecked.Set(contains);
        }, selectedSet);

        UseEffect(() =>
        {
            var next = new HashSet<string>(selectedSet.Value, StringComparer.OrdinalIgnoreCase);
            if (isChecked.Value) next.Add(name);
            else next.Remove(name);

            if (!next.SetEquals(selectedSet.Value)) selectedSet.Set(next);
        }, isChecked);

        return Layout.Horizontal().AlignContent(Align.Left)
            | isChecked.ToBoolInput(name)
            | (badge != null ? new Badge(badge).Variant(BadgeVariant.Outline).Small() : null);
    }
}

public class ImportFromVaultDialog(
    IState<bool> dialogOpen,
    VaultCatalogItem? projectItem,
    IVaultService vaultService,
    IClientProvider client,
    Action onImported) : ViewBase
{
    public override object? Build()
    {
        var repoMappings = UseState(() =>
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var initialMappings = new Dictionary<string, string>();
            if (projectItem != null)
            {
                foreach (var repo in projectItem.Repos)
                {
                    var repoKey = !string.IsNullOrEmpty(repo.Owner) && repo.Owner != "local" && repo.Owner != "default"
                        ? $"{repo.Owner}/{repo.Name}"
                        : repo.Name;
                    initialMappings[repoKey] = Path.Combine(homeDir, "git", repo.Name);
                }
            }
            return initialMappings;
        });

        var selectedSkills = UseState<HashSet<string>>(() =>
            projectItem != null
                ? new HashSet<string>(projectItem.SkillNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var selectedMcps = UseState<HashSet<string>>(() =>
            projectItem != null
                ? new HashSet<string>(projectItem.McpServerNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var selectedMemories = UseState<HashSet<string>>(() =>
            projectItem != null
                ? new HashSet<string>(projectItem.MemoryFileNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var selectedReviewActions = UseState<HashSet<string>>(() =>
            projectItem != null
                ? new HashSet<string>(projectItem.ReviewActionNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var selectedVerifications = UseState<HashSet<string>>(() =>
            projectItem != null
                ? new HashSet<string>(projectItem.VerificationNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var importPermissions = UseState(true);

        var isLoading = UseState(false);
        var errorMessage = UseState<string?>(null);

        if (!dialogOpen.Value || projectItem == null) return null;

        async Task HandleImport()
        {
            if (isLoading.Value) return;

            isLoading.Set(true);
            errorMessage.Set(null);

            var request = new VaultImportRequest
            {
                ProjectName = projectItem.Name,
                LocalRepoMappings = repoMappings.Value,
                SelectedSkills = selectedSkills.Value.ToList(),
                SelectedMcps = selectedMcps.Value.ToList(),
                SelectedMemories = selectedMemories.Value.ToList(),
                SelectedReviewActions = selectedReviewActions.Value.ToList(),
                SelectedVerifications = selectedVerifications.Value.ToList(),
                ImportPermissions = importPermissions.Value
            };

            var result = await vaultService.ImportProjectAsync(request);
            isLoading.Set(false);

            if (result.Success)
            {
                dialogOpen.Set(false);
                client.Toast(result.Message, "Project Imported");
                onImported();
            }
            else
            {
                errorMessage.Set(result.ErrorMessage ?? result.Message);
            }
        }

        var repoInputs = Layout.Vertical();
        if (projectItem.Repos.Count > 0)
        {
            repoInputs |= Text.Block("Local Folder Mappings").Small().Bold();
            foreach (var repo in projectItem.Repos)
            {
                repoInputs |= new ImportRepoPathRow(repo, repoMappings);
            }
        }

        var assetChecklist = Layout.Vertical();

        // Skills
        if (projectItem.SkillNames.Count > 0)
        {
            var skillsList = Layout.Vertical();
            foreach (var s in projectItem.SkillNames)
            {
                skillsList |= new ImportAssetItemRow(s, selectedSkills, "Skill");
            }
            assetChecklist |= new Expandable($"Skills ({projectItem.SkillNames.Count})", skillsList).Small().Open(true);
        }

        // MCP Servers
        if (projectItem.McpServerNames.Count > 0)
        {
            var mcpsList = Layout.Vertical();
            foreach (var m in projectItem.McpServerNames)
            {
                mcpsList |= new ImportAssetItemRow(m, selectedMcps, "MCP");
            }
            assetChecklist |= new Expandable($"MCP Servers ({projectItem.McpServerNames.Count})", mcpsList).Small().Open(true);
        }

        // Memories
        if (projectItem.MemoryFileNames.Count > 0)
        {
            var memsList = Layout.Vertical();
            foreach (var mem in projectItem.MemoryFileNames)
            {
                memsList |= new ImportAssetItemRow(mem, selectedMemories, "Memory");
            }
            assetChecklist |= new Expandable($"Project Memories ({projectItem.MemoryFileNames.Count})", memsList).Small().Open(true);
        }

        // Review Actions
        if (projectItem.ReviewActionNames.Count > 0)
        {
            var actionsList = Layout.Vertical();
            foreach (var a in projectItem.ReviewActionNames)
            {
                actionsList |= new ImportAssetItemRow(a, selectedReviewActions, "Action");
            }
            assetChecklist |= new Expandable($"Review Actions ({projectItem.ReviewActionNames.Count})", actionsList).Small().Open(true);
        }

        // Verifications
        if (projectItem.VerificationNames.Count > 0)
        {
            var verifsList = Layout.Vertical();
            foreach (var v in projectItem.VerificationNames)
            {
                verifsList |= new ImportAssetItemRow(v, selectedVerifications, "Verification");
            }
            assetChecklist |= new Expandable($"Verifications ({projectItem.VerificationNames.Count})", verifsList).Small().Open(true);
        }

        // Permissions
        assetChecklist |= importPermissions.ToBoolInput("Import Security & Permissions Policies");

        var form = Layout.Vertical()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | Text.Block($"Project: {projectItem.Name}").Bold()
                | new Badge($"v{projectItem.RemoteVersion}").Variant(BadgeVariant.Secondary))
            | (!string.IsNullOrEmpty(projectItem.Description) ? Text.P(projectItem.Description).Small().Muted() : null)
            | (!string.IsNullOrEmpty(projectItem.LatestChangelog) ? Text.P($"Changelog: {projectItem.LatestChangelog}").Small().Muted() : null)
            | repoInputs
            | Text.Block("Assets to Import").Small().Bold()
            | assetChecklist
            | (errorMessage.Value != null ? Callout.Error(errorMessage.Value) : null);

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader($"Import '{projectItem.Name}' from Vault"),
            new DialogBody(form),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button("Import Project")
                    .Icon(Icons.Download)
                    .Primary()
                    .Loading(isLoading.Value)
                    .Disabled(isLoading.Value)
                    .OnClick(async () => await HandleImport())
            )
        );
    }
}

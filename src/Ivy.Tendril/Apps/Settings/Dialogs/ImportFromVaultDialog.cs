using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class ImportRepoPathRow(string repo, IState<Dictionary<string, string>> repoMappings) : ViewBase
{
    public override object Build()
    {
        var inputState = UseState(() => repoMappings.Value.TryGetValue(repo, out var p) ? p : "");

        UseEffect(() =>
        {
            if (repoMappings.Value.TryGetValue(repo, out var val) && val != inputState.Value)
            {
                inputState.Set(val);
            }
        }, repoMappings);

        UseEffect(() =>
        {
            var updated = new Dictionary<string, string>(repoMappings.Value) { [repo] = inputState.Value };
            repoMappings.Set(updated);
        }, inputState);

        return inputState.ToTextInput().WithField().Label(repo);
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
                    var repoName = repo.Contains('/') ? repo.Split('/')[^1] : repo;
                    initialMappings[repo] = Path.Combine(homeDir, "git", repoName);
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
            repoInputs |= Text.Block("Map repositories to local folders:").Small().Bold();
            foreach (var repo in projectItem.Repos)
            {
                repoInputs |= new ImportRepoPathRow(repo, repoMappings);
            }
        }

        var assetChecklist = Layout.Vertical();

        // Skills
        if (projectItem.SkillNames.Count > 0)
        {
            assetChecklist |= Text.Block("Skills to import:").Small().Bold();
            foreach (var s in projectItem.SkillNames)
            {
                assetChecklist |= new ImportAssetItemRow(s, selectedSkills, "Skill");
            }
        }

        // MCP Servers
        if (projectItem.McpServerNames.Count > 0)
        {
            assetChecklist |= Text.Block("MCP Servers to import:").Small().Bold();
            foreach (var m in projectItem.McpServerNames)
            {
                assetChecklist |= new ImportAssetItemRow(m, selectedMcps, "MCP");
            }
        }

        // Memories
        if (projectItem.MemoryFileNames.Count > 0)
        {
            assetChecklist |= Text.Block("Project Memories to import:").Small().Bold();
            foreach (var mem in projectItem.MemoryFileNames)
            {
                assetChecklist |= new ImportAssetItemRow(mem, selectedMemories, "Memory");
            }
        }

        // Review Actions
        if (projectItem.ReviewActionNames.Count > 0)
        {
            assetChecklist |= Text.Block("Review Actions to import:").Small().Bold();
            foreach (var a in projectItem.ReviewActionNames)
            {
                assetChecklist |= new ImportAssetItemRow(a, selectedReviewActions, "Action");
            }
        }

        // Verifications
        if (projectItem.VerificationNames.Count > 0)
        {
            assetChecklist |= Text.Block("Verifications to import:").Small().Bold();
            foreach (var v in projectItem.VerificationNames)
            {
                assetChecklist |= new ImportAssetItemRow(v, selectedVerifications, "Verification");
            }
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

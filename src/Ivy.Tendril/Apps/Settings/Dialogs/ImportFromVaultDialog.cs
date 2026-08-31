using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class ImportRepoPathRow(
    VaultRepoRef repoRef,
    IState<Dictionary<string, string>> repoMappings,
    List<ProjectConfig> existingProjects) : ViewBase
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

        // Check if any other existing project is already using this path
        var sharingProject = existingProjects.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(inputState.Value) &&
            p.Repos.Any(r => r.Path.TrimEnd('/', '\\').Equals(inputState.Value.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)));

        return Layout.Vertical()
            | inputState.ToTextInput().WithField().Label(label)
            | (!exists && !string.IsNullOrWhiteSpace(repoRef.RemoteUrl)
                ? Text.Block("⚡ Folder not found locally — will auto-clone from remote repository upon import.").Small().Muted()
                : null)
            | (sharingProject != null
                ? Text.Block($"ℹ Folder is also used by existing project '{sharingProject.Name}'. Both projects will point to the same directory.").Small().Muted()
                : null);
    }
}

public class ImportCategoryActionsHeader(
    string title,
    int count,
    List<string> allItems,
    IState<HashSet<string>> selectedSet) : ViewBase
{
    public override object Build()
    {
        if (count == 0) return Text.Block($"No {title.ToLowerInvariant()} in this vault project.").Small().Muted();

        return Layout.Horizontal().AlignContent(Align.SpaceBetween)
            | Text.Block($"{title} ({count})").Bold().Small()
            | (Layout.Horizontal().AlignContent(Align.Right)
                | new Button("Select All").Small().Ghost().OnClick(() =>
                {
                    selectedSet.Set(new HashSet<string>(allItems, StringComparer.OrdinalIgnoreCase));
                })
                | new Button("Deselect All").Small().Ghost().OnClick(() =>
                {
                    selectedSet.Set(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }));
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
        var config = UseService<IConfigService>();

        var targetProjectName = UseState(() =>
        {
            if (projectItem == null) return "";
            var existingProjects = config.Settings.Projects;
            var hasExisting = existingProjects.Any(p => p.Name.Equals(projectItem.Name, StringComparison.OrdinalIgnoreCase));
            if (hasExisting && projectItem.SyncStatus == VaultItemSyncStatus.Conflict)
            {
                int index = 2;
                while (existingProjects.Any(p => p.Name.Equals($"{projectItem.Name}-{index}", StringComparison.OrdinalIgnoreCase)))
                {
                    index++;
                }
                return $"{projectItem.Name}-{index}";
            }
            return projectItem.Name;
        });

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

        var existingProjects = config.Settings.Projects;
        var hasExistingName = existingProjects.Any(p => p.Name.Equals(projectItem.Name, StringComparison.OrdinalIgnoreCase));

        async Task HandleImport()
        {
            var finalName = targetProjectName.Value.Trim();
            if (isLoading.Value || string.IsNullOrWhiteSpace(finalName)) return;

            // Prevent importing with a conflicting name without user awareness
            if (hasExistingName && finalName.Equals(projectItem.Name, StringComparison.OrdinalIgnoreCase) && projectItem.SyncStatus == VaultItemSyncStatus.Conflict)
            {
                errorMessage.Set($"A local project named '{projectItem.Name}' already exists. Please choose a different local project name above.");
                return;
            }

            isLoading.Set(true);
            errorMessage.Set(null);

            var request = new VaultImportRequest
            {
                ProjectName = projectItem.Name,
                TargetLocalProjectName = finalName,
                SourceVaultId = projectItem.SourceVaultId,
                LocalRepoMappings = repoMappings.Value,
                SelectedSkills = selectedSkills.Value.ToList(),
                SelectedMcps = selectedMcps.Value.ToList(),
                SelectedMemories = selectedMemories.Value.ToList(),
                SelectedReviewActions = selectedReviewActions.Value.ToList(),
                SelectedVerifications = selectedVerifications.Value.ToList(),
                ImportPermissions = importPermissions.Value
            };

            var result = await vaultService.ImportProjectAsync(request, projectItem.SourceVaultId);
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
                repoInputs |= new ImportRepoPathRow(repo, repoMappings, existingProjects);
            }
        }

        var assetChecklist = Layout.Vertical();

        // Skills
        var skillsList = Layout.Vertical();
        if (projectItem.SkillNames.Count > 0)
        {
            skillsList |= new ImportCategoryActionsHeader("Skills", projectItem.SkillNames.Count, projectItem.SkillNames, selectedSkills);
            foreach (var s in projectItem.SkillNames)
                skillsList |= new ImportAssetItemRow(s, selectedSkills, "Skill");
        }
        else
        {
            skillsList |= Text.Block("No custom skills in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable($"Skills ({projectItem.SkillNames.Count})", skillsList).Small().Open(projectItem.SkillNames.Count > 0);

        // MCP Servers
        var mcpsList = Layout.Vertical();
        if (projectItem.McpServerNames.Count > 0)
        {
            mcpsList |= new ImportCategoryActionsHeader("MCP Servers", projectItem.McpServerNames.Count, projectItem.McpServerNames, selectedMcps);
            foreach (var m in projectItem.McpServerNames)
                mcpsList |= new ImportAssetItemRow(m, selectedMcps, "MCP");
        }
        else
        {
            mcpsList |= Text.Block("No MCP servers in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable($"MCP Servers ({projectItem.McpServerNames.Count})", mcpsList).Small().Open(projectItem.McpServerNames.Count > 0);

        // Memories
        var memsList = Layout.Vertical();
        if (projectItem.MemoryFileNames.Count > 0)
        {
            memsList |= new ImportCategoryActionsHeader("Project Memories", projectItem.MemoryFileNames.Count, projectItem.MemoryFileNames, selectedMemories);
            foreach (var mem in projectItem.MemoryFileNames)
                memsList |= new ImportAssetItemRow(mem, selectedMemories, "Memory");
        }
        else
        {
            memsList |= Text.Block("No project memory markdown files in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable($"Project Memories ({projectItem.MemoryFileNames.Count})", memsList).Small().Open(projectItem.MemoryFileNames.Count > 0);

        // Review Actions
        var actionsList = Layout.Vertical();
        if (projectItem.ReviewActionNames.Count > 0)
        {
            actionsList |= new ImportCategoryActionsHeader("Review Actions", projectItem.ReviewActionNames.Count, projectItem.ReviewActionNames, selectedReviewActions);
            foreach (var a in projectItem.ReviewActionNames)
                actionsList |= new ImportAssetItemRow(a, selectedReviewActions, "Action");
        }
        else
        {
            actionsList |= Text.Block("No review actions in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable($"Review Actions ({projectItem.ReviewActionNames.Count})", actionsList).Small().Open(projectItem.ReviewActionNames.Count > 0);

        // Verifications
        var verifsList = Layout.Vertical();
        if (projectItem.VerificationNames.Count > 0)
        {
            verifsList |= new ImportCategoryActionsHeader("Verifications", projectItem.VerificationNames.Count, projectItem.VerificationNames, selectedVerifications);
            foreach (var v in projectItem.VerificationNames)
                verifsList |= new ImportAssetItemRow(v, selectedVerifications, "Verification");
        }
        else
        {
            verifsList |= Text.Block("No verifications in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable($"Verifications ({projectItem.VerificationNames.Count})", verifsList).Small().Open(projectItem.VerificationNames.Count > 0);

        // Permissions
        assetChecklist |= importPermissions.ToBoolInput("Import Security & Permissions Policies");

        var conflictCallout = (hasExistingName && projectItem.SyncStatus == VaultItemSyncStatus.Conflict)
            ? Callout.Warning($"A local project named '{projectItem.Name}' already exists. We've suggested '{targetProjectName.Value}' as the local project name to avoid overwriting your existing project.")
            : null;

        var form = Layout.Vertical()
            | conflictCallout
            | targetProjectName.ToTextInput().WithField().Label("Local Project Name")
            | (Layout.Horizontal().AlignContent(Align.Left)
                | Text.Block($"Vault Source: {projectItem.Name}").Bold().Small()
                | new Badge($"v{projectItem.RemoteVersion}").Variant(BadgeVariant.Secondary).Small())
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
                    .Disabled(isLoading.Value || string.IsNullOrWhiteSpace(targetProjectName.Value))
                    .OnClick(async () => await HandleImport())
            )
        );
    }
}

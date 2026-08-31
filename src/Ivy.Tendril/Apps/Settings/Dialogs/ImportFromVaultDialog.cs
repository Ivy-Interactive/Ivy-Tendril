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

public class CategorySelectionToolbar(
    List<string> allItems,
    IState<HashSet<string>> selectedSet) : ViewBase
{
    public override object? Build()
    {
        if (allItems.Count <= 1) return null;

        return Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Select All").Small().Ghost().OnClick(() =>
            {
                selectedSet.Set(new HashSet<string>(allItems, StringComparer.OrdinalIgnoreCase));
            })
            | new Button("Deselect All").Small().Ghost().OnClick(() =>
            {
                selectedSet.Set(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            });
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
    IState<VaultCatalogItem?> selectedProjectItem,
    IVaultService vaultService,
    IClientProvider client,
    Action onImported) : ViewBase
{
    private static string ComputeSuggestedName(string baseName, List<ProjectConfig> existing)
    {
        if (!existing.Any(p => p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        int index = 2;
        while (existing.Any(p => p.Name.Equals($"{baseName}-{index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }
        return $"{baseName}-{index}";
    }

    public override object? Build()
    {
        var targetProjectName = UseState("");
        var repoMappings = UseState<Dictionary<string, string>>(() => new());
        var selectedSkills = UseState<HashSet<string>>(() => new(StringComparer.OrdinalIgnoreCase));
        var selectedMcps = UseState<HashSet<string>>(() => new(StringComparer.OrdinalIgnoreCase));
        var selectedMemories = UseState<HashSet<string>>(() => new(StringComparer.OrdinalIgnoreCase));
        var selectedReviewActions = UseState<HashSet<string>>(() => new(StringComparer.OrdinalIgnoreCase));
        var selectedVerifications = UseState<HashSet<string>>(() => new(StringComparer.OrdinalIgnoreCase));
        var importPermissions = UseState(true);
        var isLoading = UseState(false);
        var errorMessage = UseState<string?>(null);

        var config = UseService<IConfigService>();
        var projectItem = selectedProjectItem.Value;

        var defaultSuggested = projectItem != null
            ? ComputeSuggestedName(projectItem.Name, config.Settings.Projects)
            : "";

        UseEffect(() =>
        {
            if (dialogOpen.Value && selectedProjectItem.Value != null)
            {
                var item = selectedProjectItem.Value;
                var existing = config.Settings.Projects;
                var suggested = ComputeSuggestedName(item.Name, existing);
                targetProjectName.Set(suggested);
                targetProjectName.Set(suggested);

                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var initialMappings = new Dictionary<string, string>();
                foreach (var repo in item.Repos)
                {
                    var repoKey = !string.IsNullOrEmpty(repo.Owner) && repo.Owner != "local" && repo.Owner != "default"
                        ? $"{repo.Owner}/{repo.Name}"
                        : repo.Name;
                    initialMappings[repoKey] = Path.Combine(homeDir, "git", repo.Name);
                }
                repoMappings.Set(initialMappings);

                selectedSkills.Set(new HashSet<string>(item.SkillNames, StringComparer.OrdinalIgnoreCase));
                selectedMcps.Set(new HashSet<string>(item.McpServerNames, StringComparer.OrdinalIgnoreCase));
                selectedMemories.Set(new HashSet<string>(item.MemoryFileNames, StringComparer.OrdinalIgnoreCase));
                selectedReviewActions.Set(new HashSet<string>(item.ReviewActionNames, StringComparer.OrdinalIgnoreCase));
                selectedVerifications.Set(new HashSet<string>(item.VerificationNames, StringComparer.OrdinalIgnoreCase));
                errorMessage.Set(null);
            }
        }, dialogOpen, selectedProjectItem);

        if (!dialogOpen.Value || projectItem == null) return null;

        var effectiveProjectName = !string.IsNullOrWhiteSpace(targetProjectName.Value)
            ? targetProjectName.Value.Trim()
            : defaultSuggested;

        var existingProjects = config.Settings.Projects;
        var isNameInUse = existingProjects.Any(p => p.Name.Equals(effectiveProjectName, StringComparison.OrdinalIgnoreCase));
        var hasOriginalCollision = existingProjects.Any(p => p.Name.Equals(projectItem.Name, StringComparison.OrdinalIgnoreCase));

        async Task HandleImport()
        {
            var finalName = !string.IsNullOrWhiteSpace(targetProjectName.Value) ? targetProjectName.Value.Trim() : defaultSuggested;
            if (isLoading.Value || string.IsNullOrWhiteSpace(finalName)) return;

            if (isNameInUse && projectItem.SyncStatus != VaultItemSyncStatus.UpdateAvailable)
            {
                errorMessage.Set($"A local project named '{finalName}' already exists. Please pick a different name (e.g. {ComputeSuggestedName(finalName, existingProjects)}).");
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
                targetProjectName.Set("");
                client.Toast(result.Message, "Project Imported");
                onImported();
            }
            else
            {
                errorMessage.Set(result.ErrorMessage ?? result.Message);
            }
        }

        var repoSection = Layout.Vertical();
        if (projectItem.Repos.Count > 0)
        {
            var repoList = Layout.Vertical();
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var repo in projectItem.Repos)
            {
                var localPath = Path.Combine(homeDir, "git", repo.Name);
                var exists = Directory.Exists(localPath);
                var repoTitle = !string.IsNullOrEmpty(repo.Owner) && repo.Owner != "local" && repo.Owner != "default"
                    ? $"{repo.Owner}/{repo.Name}"
                    : repo.Name;

                repoList |= Layout.Horizontal().AlignContent(Align.SpaceBetween)
                    | (Layout.Horizontal().AlignContent(Align.Left)
                        | Icons.FolderGit2.ToIcon()
                        | Text.Block(repoTitle).Bold()
                        | Text.Monospaced(localPath).Small().Muted())
                    | (exists
                        ? new Badge("✓ Existing Local Folder").Variant(BadgeVariant.Secondary).Small()
                        : new Badge("Will Clone from GitHub").Variant(BadgeVariant.Outline).Small());
            }

            repoSection |= Text.Block("Repositories").Small().Bold();
            repoSection |= Text.Block("Will link to existing local folders on disk or auto-clone missing repositories from GitHub.").Small().Muted();
            repoSection |= repoList;
        }

        var assetChecklist = Layout.Vertical();

        // Skills
        var skillsHeader = Layout.Horizontal().AlignContent(Align.Left)
            | Text.Block("Skills")
            | new Badge(projectItem.SkillNames.Count.ToString()).Variant(BadgeVariant.Secondary).Small();
        var skillsList = Layout.Vertical();
        if (projectItem.SkillNames.Count > 0)
        {
            skillsList |= new CategorySelectionToolbar(projectItem.SkillNames, selectedSkills);
            foreach (var s in projectItem.SkillNames)
                skillsList |= new ImportAssetItemRow(s, selectedSkills, "Skill");
        }
        else
        {
            skillsList |= Text.Block("No custom skills in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable(skillsHeader, skillsList).Small().Open(projectItem.SkillNames.Count > 0);

        // MCP Servers
        var mcpsHeader = Layout.Horizontal().AlignContent(Align.Left)
            | Text.Block("MCP Servers")
            | new Badge(projectItem.McpServerNames.Count.ToString()).Variant(BadgeVariant.Secondary).Small();
        var mcpsList = Layout.Vertical();
        if (projectItem.McpServerNames.Count > 0)
        {
            mcpsList |= new CategorySelectionToolbar(projectItem.McpServerNames, selectedMcps);
            foreach (var m in projectItem.McpServerNames)
                mcpsList |= new ImportAssetItemRow(m, selectedMcps, "MCP");
        }
        else
        {
            mcpsList |= Text.Block("No MCP servers in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable(mcpsHeader, mcpsList).Small().Open(projectItem.McpServerNames.Count > 0);

        // Memories
        var memsHeader = Layout.Horizontal().AlignContent(Align.Left)
            | Text.Block("Project Memories")
            | new Badge(projectItem.MemoryFileNames.Count.ToString()).Variant(BadgeVariant.Secondary).Small();
        var memsList = Layout.Vertical();
        if (projectItem.MemoryFileNames.Count > 0)
        {
            memsList |= new CategorySelectionToolbar(projectItem.MemoryFileNames, selectedMemories);
            foreach (var mem in projectItem.MemoryFileNames)
                memsList |= new ImportAssetItemRow(mem, selectedMemories, "Memory");
        }
        else
        {
            memsList |= Text.Block("No project memory markdown files in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable(memsHeader, memsList).Small().Open(projectItem.MemoryFileNames.Count > 0);

        // Review Actions
        var actionsHeader = Layout.Horizontal().AlignContent(Align.Left)
            | Text.Block("Review Actions")
            | new Badge(projectItem.ReviewActionNames.Count.ToString()).Variant(BadgeVariant.Secondary).Small();
        var actionsList = Layout.Vertical();
        if (projectItem.ReviewActionNames.Count > 0)
        {
            actionsList |= new CategorySelectionToolbar(projectItem.ReviewActionNames, selectedReviewActions);
            foreach (var a in projectItem.ReviewActionNames)
                actionsList |= new ImportAssetItemRow(a, selectedReviewActions, "Action");
        }
        else
        {
            actionsList |= Text.Block("No review actions in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable(actionsHeader, actionsList).Small().Open(projectItem.ReviewActionNames.Count > 0);

        // Verifications
        var verifsHeader = Layout.Horizontal().AlignContent(Align.Left)
            | Text.Block("Verifications")
            | new Badge(projectItem.VerificationNames.Count.ToString()).Variant(BadgeVariant.Secondary).Small();
        var verifsList = Layout.Vertical();
        if (projectItem.VerificationNames.Count > 0)
        {
            verifsList |= new CategorySelectionToolbar(projectItem.VerificationNames, selectedVerifications);
            foreach (var v in projectItem.VerificationNames)
                verifsList |= new ImportAssetItemRow(v, selectedVerifications, "Verification");
        }
        else
        {
            verifsList |= Text.Block("No verifications in this vault project.").Small().Muted();
        }
        assetChecklist |= new Expandable(verifsHeader, verifsList).Small().Open(projectItem.VerificationNames.Count > 0);

        // Permissions
        assetChecklist |= importPermissions.ToBoolInput("Import Security & Permissions Policies");

        var collisionNotice = (hasOriginalCollision && effectiveProjectName != projectItem.Name)
            ? Callout.Info($"A local project named '{projectItem.Name}' already exists. We've suggested '{effectiveProjectName}' for this import to avoid conflicts.")
            : null;

        var nameInput = targetProjectName.ToTextInput(defaultSuggested)
            .WithField().Label("Local Project Name");

        var form = Layout.Vertical()
            | collisionNotice
            | nameInput
            | (Layout.Horizontal().AlignContent(Align.Left)
                | Text.Block($"Vault Source: {projectItem.Name}").Bold().Small()
                | new Badge($"v{projectItem.RemoteVersion}").Variant(BadgeVariant.Secondary).Small())
            | (!string.IsNullOrEmpty(projectItem.Description) ? Text.P(projectItem.Description).Small().Muted() : null)
            | (!string.IsNullOrEmpty(projectItem.LatestChangelog) ? Text.P($"Changelog: {projectItem.LatestChangelog}").Small().Muted() : null)
            | repoSection
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
                    .Disabled(isLoading.Value || string.IsNullOrWhiteSpace(effectiveProjectName))
                    .OnClick(async () => await HandleImport())
            )
        );
    }
}

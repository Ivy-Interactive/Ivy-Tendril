using System;
using System.Collections.Generic;
using System.Linq;
using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.Settings.Blades;
using Ivy.Tendril.Apps.Settings.Dialogs;
using Ivy.Tendril.Apps.Settings.Sheets;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class ProjectDetailView(
    int projectIndex,
    List<ProjectConfig> projects,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken,
    Action? onDeleteProject = null) : ViewBase
{
    public override object? Build()
    {
        Context.TryUseService<TendrilArgs>(out var tendrilArgs);

        // Inline Name & Color Editing State
        var isEditingName = UseState(false);
        var editName = UseState(() => projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? config.Settings.Projects[projectIndex].Name : "");
        var projectColor = UseState<Colors?>(() => projectIndex >= 0 && projectIndex < config.Settings.Projects.Count && Enum.TryParse<Colors>(config.Settings.Projects[projectIndex].Color, true, out var c) ? c : Colors.Slate);

        // Agent Behavior State
        var autoImplement = UseState(() => projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? (config.Settings.Projects[projectIndex].AutoImplementPlans == "Auto-Implement Plans" ? "Auto-Implement Plans" : "Always Ask Review") : "Always Ask Review");

        // Repositories State
        var repos = UseState(() => projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? new List<RepoRef>(config.Settings.Projects[projectIndex].Repos) : new List<RepoRef>());

        // Review Actions State
        var reviewActions = UseState(() => projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? new List<ReviewActionConfig>(config.Settings.Projects[projectIndex].ReviewActions) : new List<ReviewActionConfig>());

        // Verifications State
        var verifications = UseState(() => projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? new List<ProjectVerificationRef>(config.Settings.Projects[projectIndex].Verifications) : new List<ProjectVerificationRef>());

        // MCP & Skills States
        var mcpServers = UseState(() => projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? new List<ProjectMcpServerRef>(config.Settings.Projects[projectIndex].McpServers) : new List<ProjectMcpServerRef>());
        var skills = UseState(() => projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? new List<ProjectSkillRef>(config.Settings.Projects[projectIndex].Skills) : new List<ProjectSkillRef>());
        var memoryRefresh = UseState(0);

        // Triggers
        var (reviewActionTrigger, showReviewActionTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new OnboardingEditReviewActionDialog(isOpen, existingIndex, reviewActions));

        var (verificationTrigger, showVerificationTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new OnboardingEditVerificationDialog(isOpen, existingIndex, config, client, refreshToken, projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? config.Settings.Projects[projectIndex].Name : ""));

        var (mcpSheet, openMcpSheet) = UseTrigger((IState<bool> isOpen, int? editingIndex) =>
            new EditMcpServerSheet(isOpen, editingIndex, mcpServers));

        var (skillSheet, openSkillSheet) = UseTrigger((IState<bool> isOpen, int? editingIndex) =>
            new EditSkillSheet(isOpen, editingIndex, skills));

        var (memorySheet, openMemorySheet) = UseTrigger((IState<bool> isOpen, string? editingFileName) =>
            new EditProjectMemorySheet(isOpen, config.TendrilHome, projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? config.Settings.Projects[projectIndex].Name : "", editingFileName, memoryRefresh));

        var (importMcpDialog, openImportMcpDialog) = UseTrigger((IState<bool> isOpen) =>
            new ImportRepoAssetsDialog(isOpen, ImportAssetKind.McpServers, projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? config.Settings.Projects[projectIndex].Name : "", repos.Value, config, client, mcpServers: mcpServers));

        var (importSkillsDialog, openImportSkillsDialog) = UseTrigger((IState<bool> isOpen) =>
            new ImportRepoAssetsDialog(isOpen, ImportAssetKind.Skills, projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? config.Settings.Projects[projectIndex].Name : "", repos.Value, config, client, skills: skills));

        var isBeta = BetaHelper.IsBeta(tendrilArgs, config);

        // Auto-save settings on state changes
        void SaveProjectChanges()
        {
            var activeProjects = config.Settings.Projects;
            if (projectIndex < 0 || projectIndex >= activeProjects.Count) return;

            var currentProj = activeProjects[projectIndex];
            var changed = false;

            var colorStr = projectColor.Value?.ToString() ?? "Slate";
            if (currentProj.Color != colorStr)
            {
                currentProj.Color = colorStr;
                changed = true;
            }
            if (currentProj.AutoImplementPlans != autoImplement.Value)
            {
                currentProj.AutoImplementPlans = autoImplement.Value;
                changed = true;
            }
            if (!AreReposEqual(currentProj.Repos, repos.Value))
            {
                currentProj.Repos = new List<RepoRef>(repos.Value);
                changed = true;
            }
            if (!AreReviewActionsEqual(currentProj.ReviewActions, reviewActions.Value))
            {
                currentProj.ReviewActions = new List<ReviewActionConfig>(reviewActions.Value);
                changed = true;
            }
            if (!AreVerificationsEqual(currentProj.Verifications, verifications.Value))
            {
                currentProj.Verifications = new List<ProjectVerificationRef>(verifications.Value);
                changed = true;
            }
            if (!AreMcpServersEqual(currentProj.McpServers, mcpServers.Value))
            {
                currentProj.McpServers = new List<ProjectMcpServerRef>(mcpServers.Value);
                changed = true;
            }
            if (!AreSkillsEqual(currentProj.Skills, skills.Value))
            {
                currentProj.Skills = new List<ProjectSkillRef>(skills.Value);
                changed = true;
            }

            if (changed)
            {
                try
                {
                    config.SaveSettings();
                    refreshToken.Refresh();
                }
                catch (Exception ex)
                {
                    client.Toast($"Failed to save project settings: {ex.Message}", "Error");
                }
            }
        }

        void DeleteSkill(int idx)
        {
            var list = new List<ProjectSkillRef>(skills.Value);
            if (idx < 0 || idx >= list.Count) return;

            var sk = list[idx];
            var projName = projectIndex >= 0 && projectIndex < config.Settings.Projects.Count ? config.Settings.Projects[projectIndex].Name : "";
            if (!string.IsNullOrEmpty(projName) && !string.IsNullOrEmpty(config.TendrilHome))
            {
                var localSkillsDir = ProjectPathHelper.GetSkillsDir(config.TendrilHome, projName);
                var skillFolder = Path.Combine(localSkillsDir, sk.Name);
                if (Directory.Exists(skillFolder))
                {
                    try { Directory.Delete(skillFolder, true); } catch { }
                }
            }

            list.RemoveAt(idx);
            skills.Set(list);

            var activeProjects = config.Settings.Projects;
            if (projectIndex >= 0 && projectIndex < activeProjects.Count)
            {
                activeProjects[projectIndex].Skills = new List<ProjectSkillRef>(list);
                config.SaveSettings();
                refreshToken.Refresh();
            }
        }

        void DeleteMcpServer(int idx)
        {
            var list = new List<ProjectMcpServerRef>(mcpServers.Value);
            if (idx < 0 || idx >= list.Count) return;

            var srv = list[idx];
            list.RemoveAt(idx);
            mcpServers.Set(list);

            var activeProjects = config.Settings.Projects;
            if (projectIndex >= 0 && projectIndex < activeProjects.Count)
            {
                activeProjects[projectIndex].McpServers = new List<ProjectMcpServerRef>(list);
                config.SaveSettings();
                refreshToken.Refresh();
            }
        }

        UseEffect(SaveProjectChanges, [projectColor, autoImplement, repos, reviewActions, verifications, mcpServers, skills]);

        var currentProjectList = config.Settings.Projects;
        if (projectIndex < 0 || projectIndex >= currentProjectList.Count)
        {
            return Layout.Vertical()
                   | Text.Block("No project selected.").Muted();
        }

        var project = currentProjectList[projectIndex];

        // Color Picker Control at the top
        var colorInput = projectColor.ToColorInput().Variant(ColorInputVariant.SwatchPicker);

        // Title Header with Inline Editing & Color Picker
        var nameHeader = isEditingName.Value
            ? (Layout.Horizontal().AlignContent(Align.Left)
               | colorInput
               | editName.ToTextInput("Project name...").Small()
               | new Button().Icon(Icons.Check).Outline().Small().OnClick(() =>
               {
                   var newName = editName.Value.Trim();
                   if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(project.Name, newName, StringComparison.OrdinalIgnoreCase))
                   {
                       var oldName = project.Name;
                       ProjectPathHelper.MoveProjectDirectory(config.TendrilHome, oldName, newName);
                       project.Name = newName;
                       config.SaveSettings();
                       refreshToken.Refresh();
                       client.Toast($"Renamed project to '{newName}'", "Renamed");
                   }
                   isEditingName.Set(false);
               })
               | new Button().Icon(Icons.X).Outline().Small().OnClick(() =>
               {
                   editName.Set(project.Name);
                   isEditingName.Set(false);
               }))
            : (Layout.Horizontal().AlignContent(Align.Left)
               | colorInput
               | Text.H2(project.Name).Bold()
               | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Rename Project").OnClick(() => isEditingName.Set(true)));

        // Agent Behavior Dropdown
        var autoImplementSelect = autoImplement.ToSelectInput(new[] { "Auto-Implement Plans", "Always Ask Review" })
            .WithField().Label("Artifact Review / Auto-Implement Policy");

        Func<RepoRef, Task<RepoRef?>> cloneRemoteOnAdd = async draft =>
        {
            var kind = RepoPathValidator.Classify(draft.Path);
            if (kind == RepoPathKind.LocalPath || kind == RepoPathKind.Invalid) return draft;

            var tendrilHome = config.TendrilHome;
            var projName = project.Name;
            var owner = RepoPathValidator.ExtractOwnerName(draft.Path) ?? "default";
            var repoName = RepoPathValidator.ExtractRepoName(draft.Path) ?? Guid.NewGuid().ToString();
            var destPath = ProjectPathHelper.GetRepoPath(tendrilHome, projName, owner, repoName);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            var success = await ProcessCheckHelper.CloneRepositoryAsync(draft.Path, destPath);
            if (!success)
            {
                client.Toast($"Failed to fetch repository: {draft.Path}", "Error");
                return null;
            }

            return draft with { Path = destPath };
        };

        var innerContent = Layout.Vertical().Width(Size.Full().Max(Size.Units(160)))
            // Section 1: Header (Color Picker + Name)
            | nameHeader
            | new Separator()

            // Section 2: Repositories
            | Text.H4("Repositories").Bold()
            | new ProjectRepoPickerView(repos, onAdd: cloneRemoteOnAdd, showBaseBranchPicker: true)
            | new Separator()

            // Section 3: Review Actions
            | Text.H4("Review Actions").Bold()
            | new ReviewActionsTableView(reviewActions, idx => showReviewActionTrigger(idx))
            | new Button("Add Review Action").Icon(Icons.Plus).Outline().Small().OnClick(() => showReviewActionTrigger(null))
            | new Separator()

            // Section 4: Verifications
            | Text.H4("Verifications").Bold()
            | new ProjectVerificationsTableView(verifications, idx => showVerificationTrigger(idx))
            | new Button("Add Verification").Icon(Icons.Plus).Outline().Small().OnClick(() => showVerificationTrigger(null))
            | new Separator()

            // Section 5: Agent Behavior
            | (isBeta
                ? (object)(Layout.Vertical()
                    | Text.H4("Agent Behavior").Bold()
                    | autoImplementSelect
                    | new Separator())
                : null!)

            // Section 6: Local Permissions (MCP Tools & Servers)
            | (isBeta
                ? (object)(Layout.Vertical()
                    | Text.H4("Local Permissions").Bold()
                    | new McpServersTableView(mcpServers, repos, idx => openMcpSheet(idx), onImport: () => openImportMcpDialog(), onDelete: idx => DeleteMcpServer(idx))
                    | new Separator())
                : null!)

            // Section 7: Customizations
            | (isBeta
                ? (object)(Layout.Vertical()
                    | Text.H4("Customizations").Bold()
                    | new ProjectMemoryTableView(config.TendrilHome, project.Name, memoryRefresh, fileName => openMemorySheet(fileName))
                    | new SkillsTableView(skills, repos, idx => openSkillSheet(idx), onImport: () => openImportSkillsDialog(), onDelete: idx => DeleteSkill(idx))
                    | new Separator())
                : null!)

            // Section 8: Danger Zone
            | Text.H4("Danger Zone").Bold()
            | new Button("Delete Project").Destructive().OnClick(() =>
            {
                onDeleteProject?.Invoke();
            }).WithConfirm(
                $"Are you sure you want to delete project '{project.Name}'? This cannot be undone.",
                title: "Delete Project",
                confirmLabel: "Delete Project",
                destructive: true
            )
            | reviewActionTrigger
            | verificationTrigger
            | mcpSheet
            | skillSheet
            | memorySheet
            | importMcpDialog
            | importSkillsDialog;

        return Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full()).Height(Size.Full())
            | innerContent;
    }

    private static bool AreReposEqual(List<RepoRef> a, List<RepoRef> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Path != b[i].Path || a[i].BaseBranch != b[i].BaseBranch)
                return false;
        }
        return true;
    }

    private static bool AreReviewActionsEqual(List<ReviewActionConfig> a, List<ReviewActionConfig> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Name != b[i].Name || a[i].Command != b[i].Command || a[i].Condition != b[i].Condition)
                return false;
        }
        return true;
    }

    private static bool AreVerificationsEqual(List<ProjectVerificationRef> a, List<ProjectVerificationRef> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Name != b[i].Name || a[i].Required != b[i].Required)
                return false;
        }
        return true;
    }

    private static bool AreMcpServersEqual(List<ProjectMcpServerRef> a, List<ProjectMcpServerRef> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Name != b[i].Name || a[i].Command != b[i].Command || a[i].Disabled != b[i].Disabled)
                return false;
        }
        return true;
    }

    private static bool AreSkillsEqual(List<ProjectSkillRef> a, List<ProjectSkillRef> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Name != b[i].Name || a[i].Path != b[i].Path || a[i].Disabled != b[i].Disabled)
                return false;
        }
        return true;
    }
}

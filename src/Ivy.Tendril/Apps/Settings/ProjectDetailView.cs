using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.Settings.Blades;
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
        // Inline Name & Color Editing State
        var isEditingName = UseState(false);
        var editName = UseState(projectIndex >= 0 && projectIndex < projects.Count ? projects[projectIndex].Name : "");
        var projectColor = UseState(projectIndex >= 0 && projectIndex < projects.Count ? (projects[projectIndex].Color ?? "Slate") : "Slate");

        // Agent Behavior State
        var autoImplement = UseState(projectIndex >= 0 && projectIndex < projects.Count ? (projects[projectIndex].AutoImplementPlans == "Auto-Implement Plans" ? "Auto-Implement Plans" : "Always Ask Review") : "Always Ask Review");

        // Repositories State
        var repos = UseState(projectIndex >= 0 && projectIndex < projects.Count ? new List<RepoRef>(projects[projectIndex].Repos) : new List<RepoRef>());

        // Review Actions State
        var reviewActions = UseState(projectIndex >= 0 && projectIndex < projects.Count ? new List<ReviewActionConfig>(projects[projectIndex].ReviewActions) : new List<ReviewActionConfig>());

        // Verifications State
        var verifications = UseState(projectIndex >= 0 && projectIndex < projects.Count ? new List<ProjectVerificationRef>(projects[projectIndex].Verifications) : new List<ProjectVerificationRef>());

        // MCP & Skills States
        var mcpServers = UseState(projectIndex >= 0 && projectIndex < projects.Count ? new List<ProjectMcpServerRef>(projects[projectIndex].McpServers) : new List<ProjectMcpServerRef>());
        var skills = UseState(projectIndex >= 0 && projectIndex < projects.Count ? new List<ProjectSkillRef>(projects[projectIndex].Skills) : new List<ProjectSkillRef>());
        var memoryRefresh = UseState(0);

        // Triggers
        var (reviewActionTrigger, showReviewActionTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new OnboardingEditReviewActionDialog(isOpen, existingIndex, reviewActions));

        var (verificationTrigger, showVerificationTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new OnboardingEditVerificationDialog(isOpen, existingIndex, config, client, refreshToken, projectIndex >= 0 && projectIndex < projects.Count ? projects[projectIndex].Name : ""));

        var (mcpSheet, openMcpSheet) = UseTrigger((IState<bool> isOpen, int? editingIndex) =>
            new EditMcpServerSheet(isOpen, editingIndex, mcpServers));

        var (skillSheet, openSkillSheet) = UseTrigger((IState<bool> isOpen, int? editingIndex) =>
            new EditSkillSheet(isOpen, editingIndex, skills));

        var (memorySheet, openMemorySheet) = UseTrigger((IState<bool> isOpen, string? editingFileName) =>
            new EditProjectMemorySheet(isOpen, config.TendrilHome, projectIndex >= 0 && projectIndex < projects.Count ? projects[projectIndex].Name : "", editingFileName, memoryRefresh));

        if (projectIndex < 0 || projectIndex >= projects.Count)
        {
            return Layout.Vertical()
                   | Text.Block("No project selected.").Muted();
        }

        var project = projects[projectIndex];

        // Auto-save settings on state changes
        void SaveProjectChanges()
        {
            project.Color = projectColor.Value;
            project.AutoImplementPlans = autoImplement.Value;
            project.Repos = new List<RepoRef>(repos.Value);
            project.ReviewActions = new List<ReviewActionConfig>(reviewActions.Value);
            project.Verifications = new List<ProjectVerificationRef>(verifications.Value);
            project.McpServers = new List<ProjectMcpServerRef>(mcpServers.Value);
            project.Skills = new List<ProjectSkillRef>(skills.Value);

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

        UseEffect(() =>
        {
            if (projectIndex >= 0 && projectIndex < projects.Count)
            {
                SaveProjectChanges();
            }
        }, [projectColor, autoImplement, repos, reviewActions, verifications]);

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

        var innerContent = Layout.Vertical().Width(Size.Rem(48))
            // Section 1: Header (Color Picker + Name)
            | nameHeader
            | new Separator()

            // Section 2: Repositories
            | Text.H4("Repositories").Bold()
            | new ProjectRepoPickerView(repos, showBaseBranchPicker: true)
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
            | Text.H4("Agent Behavior").Bold()
            | autoImplementSelect
            | new Separator()

            // Section 6: Local Permissions (MCP Tools & Servers)
            | Text.H4("Local Permissions").Bold()
            | Text.Block("MCP Tools & Servers").Bold().Small()
            | new McpServersTableView(mcpServers, idx => openMcpSheet(idx))
            | new Button("Add MCP Server").Icon(Icons.Plus).Outline().Small().OnClick(() => openMcpSheet(null))
            | new Separator()

            // Section 7: Customizations
            | Text.H4("Customizations").Bold()
            | Text.Block("Project Memories").Bold().Small()
            | new ProjectMemoryTableView(config.TendrilHome, project.Name, memoryRefresh, fileName => openMemorySheet(fileName))
            | new Button("Add Project Memory").Icon(Icons.Plus).Outline().Small().OnClick(() => openMemorySheet(null))

            | Text.Block("Custom Skills").Bold().Small()
            | new SkillsTableView(skills, idx => openSkillSheet(idx))
            | new Button("Add Custom Skill").Icon(Icons.Plus).Outline().Small().OnClick(() => openSkillSheet(null))
            | new Separator()

            // Section 8: Danger Zone
            | Text.H4("Danger Zone").Bold()
            | new Button("Delete Project").Primary().OnClick(() =>
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
            | memorySheet;

        return Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full()).Height(Size.Full())
            | innerContent;
    }
}

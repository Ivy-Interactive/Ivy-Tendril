using System.Text.Json;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Settings.Blades;

public class EditProjectBladeView(
    int editIndex,
    List<ProjectConfig> projectsList,
    List<string> allVerifications,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken) : ViewBase
{
    private record VerificationItem(string Name, bool Enabled, bool Required);
    private static readonly JsonSerializerOptions VerificationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public override object? Build()
    {
        var bladeContext = UseContext<IBladeContext>();
        var editName = UseState(editIndex >= 0 && editIndex < projectsList.Count ? projectsList[editIndex].Name : "");
        var editColor = UseState<Colors?>(editIndex >= 0 && editIndex < projectsList.Count && Enum.TryParse<Colors>(projectsList[editIndex].Color, out var c) ? c : null);
        var editContext = UseState(editIndex >= 0 && editIndex < projectsList.Count ? projectsList[editIndex].Context : "");
        var editRepos = UseState(editIndex >= 0 && editIndex < projectsList.Count ? new List<RepoRef>(projectsList[editIndex].Repos) : new List<RepoRef>());
        var editVerifications = UseState(editIndex >= 0 && editIndex < projectsList.Count ? new List<ProjectVerificationRef>(projectsList[editIndex].Verifications) : new List<ProjectVerificationRef>());
        var editReviewActions = UseState(editIndex >= 0 && editIndex < projectsList.Count ? new List<ReviewActionConfig>(projectsList[editIndex].ReviewActions) : new List<ReviewActionConfig>());
        var editMcpServers = UseState(editIndex >= 0 && editIndex < projectsList.Count ? new List<ProjectMcpServerRef>(projectsList[editIndex].McpServers) : new List<ProjectMcpServerRef>());
        var editSkills = UseState(editIndex >= 0 && editIndex < projectsList.Count ? new List<ProjectSkillRef>(projectsList[editIndex].Skills) : new List<ProjectSkillRef>());
        var memoryRefresh = UseState(0);

        if (editIndex < 0 || editIndex >= projectsList.Count) return null;

        var existingNames = projectsList
            .Where((_, i) => i != editIndex)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nameError = InputSanitizer.DescribeProjectNameError(editName.Value);
        if (nameError == null && existingNames.Contains(editName.Value.Trim()))
            nameError = $"A project named '{editName.Value.Trim()}' already exists.";

        var hasInvalidRepos = RepoPathValidator.HasInvalidLocalRepos(editRepos.Value, config.TendrilHome);

        Func<RepoRef, Task<RepoRef?>> cloneRemoteOnAdd = async draft =>
        {
            var kind = RepoPathValidator.Classify(draft.Path);
            if (kind == RepoPathKind.LocalPath || kind == RepoPathKind.Invalid) return draft;

            var tendrilHome = config.TendrilHome;
            var projectName = editName.Value;
            var repoName = RepoPathValidator.ExtractRepoName(draft.Path) ?? Guid.NewGuid().ToString();
            var owner = RepoPathValidator.ExtractOwnerName(draft.Path) ?? "default";
            var destPath = ProjectPathHelper.GetRepoPath(tendrilHome, projectName, owner, repoName);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            var success = await ProcessCheckHelper.CloneRepositoryAsync(draft.Path, destPath);
            if (!success)
            {
                client.Toast($"Failed to fetch repository: {draft.Path}", "Error");
                return null;
            }

            return draft with { Path = destPath };
        };

        var allDefs = config.Settings.Verifications;
        var displayedVerifications = OrderForDisplay(editVerifications.Value, allDefs);

        var verificationsJson = JsonSerializer.Serialize(
            displayedVerifications.Select(v =>
            {
                var projectVerif = editVerifications.Value.FirstOrDefault(pv => pv.Name == v.Name);
                return new
                {
                    name = v.Name,
                    enabled = projectVerif != null,
                    required = projectVerif?.Required ?? false
                };
            }).ToList()
        );

        var sortableVerificationList = new SortableVerificationList()
            .ItemsJson(verificationsJson)
            .WithOnReorder(json =>
            {
                var indices = JsonSerializer.Deserialize<int[]>(json);
                if (indices == null) return;
                editVerifications.Set(
                    ReorderProjectVerifications(indices, displayedVerifications, editVerifications.Value));
            })
            .WithOnChange(json => editVerifications.Set(ApplyVerificationChange(json, editVerifications.Value)));

        var saveButton = new Button("Save Project").Primary()
            .Disabled(nameError != null || hasInvalidRepos)
            .OnClick(() =>
            {
                if (nameError != null || hasInvalidRepos) return;

                var project = projectsList[editIndex];
                var oldName = project.Name;
                if (!string.IsNullOrWhiteSpace(oldName) && !string.Equals(oldName, editName.Value, StringComparison.OrdinalIgnoreCase))
                {
                    ProjectPathHelper.MoveProjectDirectory(config.TendrilHome, oldName, editName.Value);
                }

                project.Name = editName.Value.Trim();
                project.Color = editColor.Value?.ToString() ?? "";
                project.Context = editContext.Value;
                project.Repos = new List<RepoRef>(editRepos.Value);
                project.Verifications = new List<ProjectVerificationRef>(editVerifications.Value);
                project.ReviewActions = new List<ReviewActionConfig>(editReviewActions.Value);
                project.McpServers = new List<ProjectMcpServerRef>(editMcpServers.Value);
                project.Skills = new List<ProjectSkillRef>(editSkills.Value);

                try
                {
                    config.SaveSettings();
                    refreshToken.Refresh();
                    client.Toast($"Project '{editName.Value}' saved", "Saved");
                    bladeContext.Pop(this);
                }
                catch (Exception ex)
                {
                    refreshToken.Refresh();
                    client.Toast($"Failed to save project: {ex.Message}", "Error");
                }
            })
            .WithTooltip(hasInvalidRepos ? "Fix or remove invalid repositories before saving" : null);

        return Layout.Vertical()
            | Layout.Tabs(
                new Tab("Basic",
                    Layout.Vertical()
                    | Text.Block("Configure the project's name, color, and AI context.").Muted().Small()
                    | editName.ToTextInput("Project name...").Invalid(nameError).WithField().Label("Name")
                    | editColor.ToColorInput().Variant(ColorInputVariant.SwatchPicker).Nullable().WithField().Label("Color")
                    | editContext.ToTextareaInput("Project context or prompt for AI agents...").Rows(4).WithField().Label("Context / Prompt")
                ),
                new Tab("Repositories",
                    Layout.Vertical()
                    | Text.Block("Manage source code repositories for this project.").Muted().Small()
                    | new ProjectRepoPickerView(editRepos, onAdd: cloneRemoteOnAdd, showBaseBranchPicker: true)
                ),
                new Tab("Memory",
                    Layout.Vertical()
                    | Text.Block("Store persistent memories about this project (e.g. stack.md, conventions.md).").Muted().Small()
                    | new ProjectMemoryTableView(config.TendrilHome, editName.Value, memoryRefresh, fileName =>
                    {
                        bladeContext.Push(this, new EditProjectMemoryBladeView(config.TendrilHome, editName.Value, fileName, memoryRefresh), title: fileName == null ? "Add Memory" : $"Edit Memory: {fileName}");
                    })
                    | new Button("Add Project Memory").Icon(Icons.Plus).Outline().OnClick(() =>
                    {
                        bladeContext.Push(this, new EditProjectMemoryBladeView(config.TendrilHome, editName.Value, null, memoryRefresh), title: "Add Project Memory");
                    })
                ),
                new Tab("MCP Servers",
                    Layout.Vertical()
                    | Text.Block("Custom Model Context Protocol (MCP) servers for this project.").Muted().Small()
                    | new McpServersTableView(editMcpServers, idx =>
                    {
                        bladeContext.Push(this, new EditMcpServerBladeView(idx, editMcpServers), title: idx == null ? "Add MCP Server" : "Edit MCP Server");
                    })
                    | new Button("Add MCP Server").Icon(Icons.Plus).Outline().OnClick(() =>
                    {
                        bladeContext.Push(this, new EditMcpServerBladeView(null, editMcpServers), title: "Add MCP Server");
                    })
                ),
                new Tab("Custom Skills",
                    Layout.Vertical()
                    | Text.Block("Custom Skills and prompt instructions for AI agents working on this project.").Muted().Small()
                    | new SkillsTableView(editSkills, idx =>
                    {
                        bladeContext.Push(this, new EditSkillBladeView(idx, editSkills), title: idx == null ? "Add Custom Skill" : "Edit Custom Skill");
                    })
                    | new Button("Add Custom Skill").Icon(Icons.Plus).Outline().OnClick(() =>
                    {
                        bladeContext.Push(this, new EditSkillBladeView(null, editSkills), title: "Add Custom Skill");
                    })
                ),
                new Tab("Review Actions",
                    Layout.Vertical()
                    | Text.Block("Quick-launch buttons shown during review to preview or run the app.").Muted().Small()
                    | new ReviewActionsTableView(editReviewActions, idx =>
                    {
                        bladeContext.Push(this, new EditReviewActionBladeView(idx, editReviewActions), title: idx == null ? "Add Review Action" : "Edit Review Action");
                    })
                    | new Button("Add Review Action").Icon(Icons.Plus).Outline().OnClick(() =>
                    {
                        bladeContext.Push(this, new EditReviewActionBladeView(null, editReviewActions), title: "Add Review Action");
                    })
                ),
                new Tab("Verifications",
                    Layout.Vertical()
                    | Text.Block("Quality checks required before plans are marked complete.").Muted().Small()
                    | new Button("Add Verification").Icon(Icons.Plus).Outline().OnClick(() =>
                    {
                        bladeContext.Push(this, new EditVerificationBladeView(config, client, refreshToken, editVerifications), title: "Add Verification");
                    })
                    | (Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Rem(20)).Width(Size.Full())
                        | sortableVerificationList)
                )
            ).Variant(TabsVariant.Content).Width(Size.Full())
            | Layout.Horizontal()
                | new Button("Cancel").Outline().OnClick(() => bladeContext.Pop(this))
                | saveButton;
    }

    internal static List<ProjectVerificationRef> ApplyVerificationChange(
        string json, List<ProjectVerificationRef> current)
    {
        var list = new List<ProjectVerificationRef>(current);
        var item = JsonSerializer.Deserialize<VerificationItem>(json, VerificationJsonOptions);
        if (item == null || string.IsNullOrEmpty(item.Name)) return list;

        var existing = list.FirstOrDefault(v => v.Name == item.Name);
        if (item.Enabled && existing == null)
            list.Add(new ProjectVerificationRef { Name = item.Name, Required = item.Required });
        else if (!item.Enabled && existing != null)
            list.Remove(existing);
        else if (existing != null)
            existing.Required = item.Required;

        return list;
    }

    public static List<VerificationConfig> OrderForDisplay(
        List<ProjectVerificationRef> projectVerifications,
        List<VerificationConfig> globalVerifications)
    {
        var globalByName = globalVerifications
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<VerificationConfig>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pv in projectVerifications)
        {
            if (globalByName.TryGetValue(pv.Name, out var v) && added.Add(v.Name))
                result.Add(v);
        }

        foreach (var v in globalVerifications)
        {
            if (added.Add(v.Name))
                result.Add(v);
        }

        return result;
    }

    public static List<ProjectVerificationRef> ReorderProjectVerifications(
        int[] newIndices,
        List<VerificationConfig> displayedVerifications,
        List<ProjectVerificationRef> currentProjectVerifications)
    {
        var currentReqByName = currentProjectVerifications
            .ToDictionary(pv => pv.Name, pv => pv.Required, StringComparer.OrdinalIgnoreCase);

        var enabledNames = new HashSet<string>(currentReqByName.Keys, StringComparer.OrdinalIgnoreCase);

        var reorderedDisplayed = newIndices
            .Where(idx => idx >= 0 && idx < displayedVerifications.Count)
            .Select(idx => displayedVerifications[idx])
            .ToList();

        var result = new List<ProjectVerificationRef>();
        var processedEnabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in reorderedDisplayed)
        {
            if (enabledNames.Contains(v.Name) && processedEnabled.Add(v.Name))
            {
                result.Add(new ProjectVerificationRef
                {
                    Name = v.Name,
                    Required = currentReqByName[v.Name]
                });
            }
        }

        foreach (var pv in currentProjectVerifications)
        {
            if (processedEnabled.Add(pv.Name))
                result.Add(pv);
        }

        return result;
    }
}

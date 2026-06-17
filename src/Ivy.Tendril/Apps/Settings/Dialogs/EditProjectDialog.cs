using System.Threading.Tasks;
using System.Text.Json;
using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class EditProjectDialog(
    IState<int?> editIndex,
    List<ProjectConfig> projects,
    List<string> allVerifications,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken) : ViewBase
{
    private readonly IState<int?> _editIndex = editIndex;
    private readonly IConfigService _config = config;
    private readonly IClientProvider _client = client;
    private readonly RefreshToken _refreshToken = refreshToken;

    public override object? Build()
    {
        var editName = UseState("");
        var editColor = UseState<Colors?>(null);

        var editContext = UseState("");
        var editRepos = UseState(new List<RepoRef>());
        var editVerifications = UseState(new List<ProjectVerificationRef>());
        var editReviewActions = UseState(new List<ReviewActionConfig>());

        var (reviewActionTriggerView, showReviewActionTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new EditReviewActionDialogContent(isOpen, existingIndex, editReviewActions));
        var (reviewActionAlertView, showReviewActionAlert) = UseAlert();

        var (verificationTriggerView, showVerificationTrigger) = UseTrigger((IState<bool> isOpen) =>
            new EditVerificationDialogContent(isOpen, editVerifications, _config, _client, _refreshToken));

        UseEffect(() =>
        {
            if (_editIndex.Value == null)
            {
                editName.Set("");
                editColor.Set(null);
                editContext.Set("");
                editRepos.Set(new List<RepoRef>());
                editVerifications.Set(new List<ProjectVerificationRef>());
                editReviewActions.Set(new List<ReviewActionConfig>());
            }
            else if (_editIndex.Value >= 0)
            {
                var projectsList = _config.Projects;
                if (_editIndex.Value.Value < projectsList.Count)
                {
                    var project = projectsList[_editIndex.Value.Value];
                    editName.Set(project.Name);
                    editColor.Set(Enum.TryParse<Colors>(project.Color, out var c) ? c : null);
                    editContext.Set(project.Context);
                    editRepos.Set(
                        new List<RepoRef>(project.Repos.Select(r => new RepoRef { Path = r.Path, PrRule = r.PrRule, BaseBranch = r.BaseBranch })));
                    editVerifications.Set(new List<ProjectVerificationRef>(
                        project.Verifications.Select(v => new ProjectVerificationRef
                        { Name = v.Name, Required = v.Required })));
                    editReviewActions.Set(new List<ReviewActionConfig>(
                        project.ReviewActions.Select(r => new ReviewActionConfig
                        { Name = r.Name, Condition = r.Condition, Command = r.Command })));
                }
            }
        }, _editIndex);

        if (_editIndex.Value == null || _editIndex.Value == -1) return null;

        var isNew = _editIndex.Value == null;

        Func<RepoRef, Task<RepoRef?>> cloneRemoteOnAdd = async draft =>
        {
            var kind = RepoPathValidator.Classify(draft.Path);
            if (kind == RepoPathKind.LocalPath || kind == RepoPathKind.Invalid) return draft;

            var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")
                              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
            var reposDir = Path.Combine(tendrilHome, "Repos");
            Directory.CreateDirectory(reposDir);

            var repoName = RepoPathValidator.ExtractRepoName(draft.Path) ?? Guid.NewGuid().ToString();
            var destPath = Path.Combine(reposDir, repoName);

            var success = await ProcessCheckHelper.CloneRepositoryAsync(draft.Path, destPath);
            if (!success)
            {
                _client.Toast($"Failed to fetch repository: {draft.Path}", "Error");
                return null;
            }

            return draft with { Path = destPath };
        };

        var verificationsJson = JsonSerializer.Serialize(
            _config.Settings.Verifications.Select(v =>
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

                var allVerifs = _config.Settings.Verifications.ToList();
                var reorderedVerifs = indices.Select(i => allVerifs[i]).ToList();
                _config.Settings.Verifications.Clear();
                _config.Settings.Verifications.AddRange(reorderedVerifs);

                try
                {
                    _config.SaveSettings();
                    _refreshToken.Refresh();
                }
                catch (Exception ex)
                {
                    _client.Toast($"Failed to reorder verifications: {ex.Message}", "Error");
                }
            })
            .WithOnChange(json =>
            {
                var item = JsonSerializer.Deserialize<VerificationItem>(json);
                if (item == null) return;

                var list = new List<ProjectVerificationRef>(editVerifications.Value);
                var existing = list.FirstOrDefault(v => v.Name == item.Name);

                if (item.Enabled && existing == null)
                {
                    list.Add(new ProjectVerificationRef { Name = item.Name, Required = item.Required });
                }
                else if (!item.Enabled && existing != null)
                {
                    list.Remove(existing);
                }
                else if (existing != null)
                {
                    existing.Required = item.Required;
                }

                editVerifications.Set(list);
            });

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var hasInvalidRepos = RepoPathValidator.HasInvalidLocalRepos(editRepos.Value, tendrilHome);

        var saveButton = new Button(isNew ? "Add" : "Save").Primary()
            .Disabled(hasInvalidRepos)
            .OnClick(async () =>
            {
                if (string.IsNullOrWhiteSpace(editName.Value)) return;

                // Perform base branch validation
                foreach (var repo in editRepos.Value)
                {
                    if (!string.IsNullOrWhiteSpace(repo.BaseBranch))
                    {
                        var isValid = await GitHelper.IsValidBranchAsync(repo.Path, repo.BaseBranch, _config.TendrilHome);
                        if (!isValid)
                        {
                            var repoName = RepoPathValidator.ExtractRepoName(repo.Path) ?? repo.Path;
                            _client.Toast($"Branch '{repo.BaseBranch}' does not exist in repository '{repoName}'", "Error");
                            return;
                        }
                    }
                }

                var projectsList = _config.Projects;
                var project = isNew ? new ProjectConfig() : projectsList[_editIndex.Value!.Value];
                var oldName = project.Name;
                var oldColor = project.Color;
                var oldContext = project.Context;
                var oldRepos = project.Repos;
                var oldVerifications = project.Verifications;
                var oldReviewActions = project.ReviewActions;
                project.Name = editName.Value;
                project.Color = editColor.Value?.ToString() ?? "";
                project.Context = editContext.Value;
                project.Repos = new List<RepoRef>(editRepos.Value);
                project.Verifications = new List<ProjectVerificationRef>(editVerifications.Value);
                project.ReviewActions = new List<ReviewActionConfig>(editReviewActions.Value);
                if (isNew) projectsList.Add(project);
                try
                {
                    _config.SaveSettings();
                    _editIndex.Set(-1);
                    _refreshToken.Refresh();
                    _client.Toast($"Project '{editName.Value}' saved", "Saved");
                }
                catch (Exception ex)
                {
                    if (isNew)
                        projectsList.Remove(project);
                    else
                    {
                        project.Name = oldName;
                        project.Color = oldColor;
                        project.Context = oldContext;
                        project.Repos = oldRepos;
                        project.Verifications = oldVerifications;
                        project.ReviewActions = oldReviewActions;
                    }
                    _refreshToken.Refresh();
                    _client.Toast($"Failed to save project: {ex.Message}", "Error");
                }
            })
            .WithTooltip(hasInvalidRepos ? "Fix or remove invalid repositories before saving" : null);

        var mainDialog = new Dialog(
            _ => _editIndex.Set(-1),
            new DialogHeader(isNew ? "Add Project" : $"Edit Project: {editName.Value}"),
            new DialogBody(
                Layout.Tabs(
                    new Tab("Basic",
                        Layout.Vertical().Gap(4)
                        | Text.Block("Configure the project's name, color, and AI context.").Muted()
                        | editName.ToTextInput("Project name...").WithField().Label("Name")
                        | editColor.ToColorInput().Variant(ColorInputVariant.SwatchPicker).Nullable().WithField().Label("Color")
                        | editContext.ToTextareaInput("Project context or prompt for AI agents (optional)...").Rows(4)
                            .WithField().Label("Context / Prompt (Optional)")
                    ),
                    new Tab("Repositories",
                        Layout.Vertical().Gap(4)
                        | Text.Block("Manage source code repositories for this project.").Muted()
                        | new ProjectRepoPickerView(editRepos, onAdd: cloneRemoteOnAdd, showBaseBranchPicker: true)
                    ),
                    new Tab("Review Actions",
                        Layout.Vertical().Gap(4)
                        | Text.Block("Commands that run during plan review (e.g., tests, linting).").Muted()
                        | new ReviewActionsTableView(editReviewActions, showReviewActionTrigger, showReviewActionAlert)
                        | new Button("Add Review Action").Icon(Icons.Plus).Outline()
                            .OnClick(() => showReviewActionTrigger(null))
                        | reviewActionTriggerView
                        | reviewActionAlertView
                    ),
                    new Tab("Verifications",
                        // Title and the Add Verification button stay pinned at the top;
                        // only the list below scrolls when it grows long.
                        Layout.Vertical().Gap(4)
                        | Text.Block("Quality checks required before plans are marked complete.").Muted()
                        | new Button("Add Verification").Icon(Icons.Plus).Outline()
                            .OnClick(() => showVerificationTrigger())
                        | (Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Rem(20)).Width(Size.Full())
                            | sortableVerificationList)
                        | verificationTriggerView
                    )
                ).Variant(TabsVariant.Content).Width(Size.Full())
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => _editIndex.Set(-1)),
                saveButton
            )
        ).Width(Size.Rem(40));

        return mainDialog;
    }

}

internal class ReviewActionsTableView(
    IState<List<ReviewActionConfig>> reviewActions,
    Action<int?> showTrigger,
    ShowAlertDelegate showAlert) : ViewBase
{
    public override object? Build()
    {
        var actions = reviewActions.Value;
        if (actions.Count == 0) return null;

        var rows = actions.Select((a, i) => new ReviewActionRow(a.Name, i)).ToList();

        return new TableBuilder<ReviewActionRow>(rows)
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<ReviewActionRow, int>(idx =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() => showTrigger(idx))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    var name = actions[idx].Name;
                    showAlert($"Are you sure you want to delete '{name}'?", result =>
                    {
                        if (result == AlertResult.Ok)
                        {
                            var list = new List<ReviewActionConfig>(actions);
                            list.RemoveAt(idx);
                            reviewActions.Set(list);
                        }
                    }, "Delete Review Action");
                })
            ))
            .Width(Size.Fit());
    }

    private record ReviewActionRow(string Name, int Index);
}

internal class EditReviewActionDialogContent(
    IState<bool> isOpen,
    int? existingIndex,
    IState<List<ReviewActionConfig>> reviewActions) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editCondition = UseState("");
        var editCommand = UseState("");

        UseEffect(() =>
        {
            var actions = reviewActions.Value;
            if (existingIndex is >= 0 && existingIndex < actions.Count)
            {
                editName.Set(actions[existingIndex.Value].Name);
                editCondition.Set(actions[existingIndex.Value].Condition);
                editCommand.Set(actions[existingIndex.Value].Command);
            }
        }, EffectTrigger.OnMount());

        var isNew = existingIndex == null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(isNew ? "Add Review Action" : "Edit Review Action"),
            new DialogBody(
                Layout.Vertical()
                | editName.ToTextInput("Action name...").WithField().Label("Name").Required()
                | editCommand.ToTextareaInput("e.g. dotnet test").Rows(2).WithField().Label("Command").Required()
                | editCondition.ToTextareaInput("e.g. ${hasChanges}").Rows(2).WithField().Label("Condition (optional)")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    if (string.IsNullOrWhiteSpace(editCommand.Value)) return;

                    var list = new List<ReviewActionConfig>(reviewActions.Value);
                    if (isNew)
                    {
                        list.Add(new ReviewActionConfig
                        {
                            Name = editName.Value,
                            Condition = editCondition.Value,
                            Command = editCommand.Value
                        });
                    }
                    else
                    {
                        list[existingIndex!.Value] = new ReviewActionConfig
                        {
                            Name = editName.Value,
                            Condition = editCondition.Value,
                            Command = editCommand.Value
                        };
                    }

                    reviewActions.Set(list);
                    isOpen.Set(false);
                })
            )
        ).Width(Size.Rem(30));
    }
}

internal record VerificationItem(string Name, bool Enabled, bool Required);

internal class EditVerificationDialogContent(
    IState<bool> isOpen,
    IState<List<ProjectVerificationRef>> projectVerifications,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editPrompt = UseState("");

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Add Verification"),
            new DialogBody(
                Layout.Vertical()
                | editName.ToTextInput("Verification name...").WithField().Label("Name")
                | editPrompt.ToCodeInput("Verification prompt...").Language(Languages.Markdown).Height(Size.Units(60)).WithField().Label("Prompt")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button("Add").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;

                    var verifications = config.Settings.Verifications;
                    verifications.Add(new VerificationConfig
                    {
                        Name = editName.Value,
                        Prompt = editPrompt.Value
                    });

                    try
                    {
                        config.SaveSettings();

                        // Also add to project's enabled verifications
                        var list = new List<ProjectVerificationRef>(projectVerifications.Value);
                        list.Add(new ProjectVerificationRef { Name = editName.Value, Required = false });
                        projectVerifications.Set(list);

                        isOpen.Set(false);
                        refreshToken.Refresh();
                        client.Toast("Verification added", "Saved");
                    }
                    catch (Exception ex)
                    {
                        verifications.RemoveAt(verifications.Count - 1);
                        refreshToken.Refresh();
                        client.Toast($"Failed to add verification: {ex.Message}", "Error");
                    }
                })
            )
        ).Width(Size.Rem(35));
    }
}

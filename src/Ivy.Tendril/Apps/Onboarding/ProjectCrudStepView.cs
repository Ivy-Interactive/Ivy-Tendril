using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Apps.Settings.Blades;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectCrudStepView(
    IState<string> projectName,
    IState<bool> isStepLoading,
    OnboardingVerificationSession session,
    Action onBack,
    Action onNext,
    string nextButtonText = "Next",
    bool showHeader = true) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();
        var initialized = UseState(false);
        var reviewActions = UseState(() =>
        {
            var p = config.Settings.Projects
                .FirstOrDefault(p => p.Name.Equals(projectName.Value, StringComparison.OrdinalIgnoreCase));
            return new List<ReviewActionConfig>(p?.ReviewActions ?? []);
        });
        var (reviewActionTriggerView, showReviewActionTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new OnboardingEditReviewActionDialog(isOpen, existingIndex, reviewActions));
        var (reviewActionAlertView, showReviewActionAlert) = UseAlert();
        var (verificationTriggerView, showVerificationTrigger) = UseTrigger((IState<bool> isOpen, string? existingVerificationName) =>
            new OnboardingEditVerificationDialog(isOpen, existingVerificationName, config, client, refreshToken, projectName.Value));
        var (verificationAlertView, showVerificationAlert) = UseAlert();

        _ = session.RefreshToken.Value;

        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(projectName.Value, StringComparison.OrdinalIgnoreCase));

        UseEffect(() =>
        {
            if (!initialized.Value) { initialized.Set(true); return; }
            if (project == null) return;
            project.ReviewActions = new List<ReviewActionConfig>(reviewActions.Value);
            config.SaveSettings();
        }, reviewActions);

        var allVerifications = config.Settings.Verifications;
        var verificationRows = (project?.Verifications ?? [])
            .Select(pv => new VerificationRow(pv.Name))
            .ToList();

        var verificationTable = new TableBuilder<VerificationRow>(verificationRows)
            .Header(t => t.Name, "Verification Name")
            .Builder(t => t.Name, f => f.Func<VerificationRow, string>(name =>
                Text.Block(name).Bold()
            ))
            .Header(t => t.Name, "")
            .Builder(t => t.Name, f => f.Func<VerificationRow, string>(vName =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() =>
                    showVerificationTrigger(vName))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    showVerificationAlert($"Are you sure you want to delete '{vName}'?", result =>
                    {
                        if (result == AlertResult.Ok)
                        {
                            allVerifications.RemoveAll(v => v.Name.Equals(vName, StringComparison.OrdinalIgnoreCase));
                            if (project != null)
                                project.Verifications.RemoveAll(v => v.Name.Equals(vName, StringComparison.OrdinalIgnoreCase));
                            try
                            {
                                config.SaveSettings();
                                client.Toast($"Verification '{vName}' deleted", "Deleted");
                                refreshToken.Refresh();
                            }
                            catch (Exception ex)
                            {
                                client.Toast($"Failed to delete: {ex.Message}", "Error");
                            }
                        }
                    }, "Delete Verification", AlertButtonSet.OkCancel);
                })
            ))
            .Width(Size.Fit());

        var buttonArea = Layout.Horizontal().Width(Size.Full())
            | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                .OnClick(onBack)
            | new Spacer()
            | new Button(nextButtonText).Secondary().Large().Icon(Icons.ArrowRight, Align.Right)
                .OnClick(onNext);

        return Layout.Vertical().Margin(0, 0, 0, 2)
               | (showHeader ? Text.H3("Review Harness") : null!)
               | Text.Muted("Review and edit the configuration generated for your project.")
               | (Layout.Vertical()
                  | Text.Block("Verifications").Bold()
                  | Text.Muted("The steps run after each plan execution to validate changes.")
                  | (verificationRows.Count > 0 ? (object)verificationTable : null!)
                  | new Button("Add Verification").Icon(Icons.Plus).Outline()
                      .OnClick(() => showVerificationTrigger(null)))
               | new Separator()
               | (Layout.Vertical()
                  | Text.Block("Review Actions").Bold()
                  | Text.Muted("Commands that makes it easy to start you project for manual testing.")
                  | new ReviewActionsTableView(reviewActions, idx => showReviewActionTrigger(idx), projectName: projectName.Value)
                  | new Button("Add Review Action").Icon(Icons.Plus).Outline()
                      .OnClick(() => showReviewActionTrigger(null)))
               | new Separator()
               | verificationTriggerView
               | verificationAlertView
               | reviewActionTriggerView
               | reviewActionAlertView
               | buttonArea;
    }

    private record VerificationRow(string Name);
}

internal class OnboardingEditReviewActionDialog(
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
                | editCommand.ToCodeInput("e.g. dotnet test").WithField().Label("Command").Required()
                | editCondition.ToCodeInput("e.g. ${hasChanges}").WithField().Label("Condition")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    if (string.IsNullOrWhiteSpace(editCommand.Value)) return;

                    var list = new List<ReviewActionConfig>(reviewActions.Value);
                    if (isNew)
                        list.Add(new ReviewActionConfig { Name = editName.Value.Trim(), Condition = editCondition.Value, Command = editCommand.Value });
                    else
                        list[existingIndex!.Value] = new ReviewActionConfig { Name = editName.Value.Trim(), Condition = editCondition.Value, Command = editCommand.Value };

                    reviewActions.Set(list);
                    isOpen.Set(false);
                })
            )
        ).Width(Size.Rem(30));
    }
}

internal class OnboardingEditVerificationDialog(
    IState<bool> isOpen,
    string? existingVerificationName,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken,
    string projectName = "",
    IState<List<ProjectVerificationRef>>? projectVerifications = null) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editPrompt = UseState("");
        UseEffect(() =>
        {
            var verifications = config.Settings.Verifications;
            var target = !string.IsNullOrEmpty(existingVerificationName)
                ? verifications.FirstOrDefault(v => v.Name.Equals(existingVerificationName, StringComparison.OrdinalIgnoreCase))
                : null;
            if (target != null)
            {
                editName.Set(target.Name);
                editPrompt.Set(target.Prompt);
            }
        }, EffectTrigger.OnMount());

        var verifications = config.Settings.Verifications;
        var isNew = string.IsNullOrEmpty(existingVerificationName);

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(isNew ? "Add Verification" : "Edit Verification"),
            new DialogBody(
                Layout.Vertical()
                | editName.ToTextInput("Verification name...").WithField().Label("Name")
                | editPrompt.ToCodeInput("Verification prompt...").Language(Languages.Markdown).Height(Size.Units(60)).WithField().Label("Prompt")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    var newName = editName.Value.Trim();
                    var newPrompt = editPrompt.Value;

                    VerificationConfig? target = null;
                    string? oldName = null;
                    string? oldPrompt = null;
                    bool renamed = false;

                    if (isNew)
                    {
                        verifications.Add(new VerificationConfig
                        {
                            Name = newName,
                            Prompt = newPrompt
                        });

                        if (!string.IsNullOrEmpty(projectName))
                        {
                            var proj = config.Settings.Projects
                                .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
                            if (proj != null && !proj.Verifications.Any(v => v.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                                proj.Verifications.Add(new ProjectVerificationRef { Name = newName, Required = true });
                        }

                        if (projectVerifications != null)
                        {
                            var list = new List<ProjectVerificationRef>(projectVerifications.Value);
                            if (!list.Any(v => v.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                            {
                                list.Add(new ProjectVerificationRef { Name = newName, Required = true });
                                projectVerifications.Set(list);
                            }
                        }
                    }
                    else
                    {
                        target = verifications.FirstOrDefault(v => v.Name.Equals(existingVerificationName, StringComparison.OrdinalIgnoreCase));
                        if (target == null) return;

                        oldName = target.Name;
                        oldPrompt = target.Prompt;
                        target.Name = newName;
                        target.Prompt = newPrompt;

                        renamed = !oldName.Equals(newName, StringComparison.OrdinalIgnoreCase);
                        if (renamed)
                        {
                            foreach (var proj in config.Settings.Projects)
                            {
                                foreach (var pv in proj.Verifications)
                                {
                                    if (pv.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        pv.Name = newName;
                                    }
                                }
                            }

                            if (projectVerifications != null)
                            {
                                var list = new List<ProjectVerificationRef>(projectVerifications.Value);
                                foreach (var pv in list)
                                {
                                    if (pv.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        pv.Name = newName;
                                    }
                                }
                                projectVerifications.Set(list);
                            }
                        }
                    }

                    try
                    {
                        config.SaveSettings();
                        isOpen.Set(false);
                        refreshToken.Refresh();
                        client.Toast("Verification saved", "Saved");
                    }
                    catch (Exception ex)
                    {
                        if (isNew)
                        {
                            verifications.RemoveAll(v => v.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrEmpty(projectName))
                            {
                                var proj = config.Settings.Projects
                                    .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
                                proj?.Verifications.RemoveAll(v => v.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
                            }
                            if (projectVerifications != null)
                            {
                                var list = new List<ProjectVerificationRef>(projectVerifications.Value);
                                list.RemoveAll(v => v.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
                                projectVerifications.Set(list);
                            }
                        }
                        else if (target != null && oldName != null)
                        {
                            target.Name = oldName;
                            target.Prompt = oldPrompt!;
                            if (renamed)
                            {
                                foreach (var proj in config.Settings.Projects)
                                {
                                    foreach (var pv in proj.Verifications)
                                    {
                                        if (pv.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            pv.Name = oldName;
                                        }
                                    }
                                }

                                if (projectVerifications != null)
                                {
                                    var list = new List<ProjectVerificationRef>(projectVerifications.Value);
                                    foreach (var pv in list)
                                    {
                                        if (pv.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            pv.Name = oldName;
                                        }
                                    }
                                    projectVerifications.Set(list);
                                }
                            }
                        }
                        refreshToken.Refresh();
                        client.Toast($"Failed to save verification: {ex.Message}", "Error");
                    }
                })
            )
        ).Width(Size.Rem(35));
    }
}

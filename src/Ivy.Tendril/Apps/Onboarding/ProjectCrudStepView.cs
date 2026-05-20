using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Apps.Setup.Dialogs;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectCrudStepView(
    IState<string> projectName,
    IState<bool> isStepLoading,
    OnboardingVerificationSession session,
    Action onBack,
    Action onNext,
    string nextButtonText = "Next") : ViewBase
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
            new EditReviewActionDialogContent(isOpen, existingIndex, reviewActions));
        var (reviewActionAlertView, showReviewActionAlert) = UseAlert();
        var (verificationTriggerView, showVerificationTrigger) = UseTrigger((IState<bool> isOpen, int? existingIndex) =>
            new OnboardingEditVerificationDialog(isOpen, existingIndex, config, client, refreshToken, projectName.Value));
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
            .Select(pv => (pv, idx: allVerifications.FindIndex(v => v.Name.Equals(pv.Name, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.idx >= 0)
            .Select(x => new VerificationRow(x.pv.Name, x.idx))
            .ToList();

        var verificationTable = new TableBuilder<VerificationRow>(verificationRows)
            .Header(t => t.Index, "")
            .Builder(t => t.Index, f => f.Func<VerificationRow, int>(idx =>
                Layout.Horizontal().Gap(1)
                | new Button().Icon(Icons.Pencil).Outline().Small().Tooltip("Edit").OnClick(() =>
                    showVerificationTrigger(idx))
                | new Button().Icon(Icons.Trash).Outline().Small().Tooltip("Delete").OnClick(() =>
                {
                    var vName = allVerifications[idx].Name;
                    showVerificationAlert($"Are you sure you want to delete '{vName}'?", result =>
                    {
                        if (result == AlertResult.Ok)
                        {
                            allVerifications.RemoveAt(idx);
                            if (project != null)
                                project.Verifications.RemoveAll(v => v.Name == vName);
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

        return Layout.Vertical().Margin(0, 0, 0, 20)
               | Text.H3("Review Harness")
               | Text.Muted("Review and edit the configuration generated for your project.")
               | (Layout.Vertical()
                  | Text.H4("Verifications")
                  | Text.Muted("The steps run after each plan execution to validate changes.")
                  | (verificationRows.Count > 0 ? (object)verificationTable : Text.Muted("No verifications configured."))
                  | new Button("Add Verification").Icon(Icons.Plus).Outline()
                      .OnClick(() => showVerificationTrigger(null)))
               | (Layout.Vertical()
                  | Text.H4("Review Actions")
                  | Text.Muted("Commands that makes it easy to start you project for manual testing.")
                  | new ReviewActionsTableView(reviewActions, showReviewActionTrigger, showReviewActionAlert)
                  | new Button("Add Review Action").Icon(Icons.Plus).Outline()
                      .OnClick(() => showReviewActionTrigger(null)))
               | verificationTriggerView
               | verificationAlertView
               | reviewActionTriggerView
               | reviewActionAlertView
               | buttonArea;
    }

    private record VerificationRow(string Name, int Index);
}

internal class OnboardingEditVerificationDialog(
    IState<bool> isOpen,
    int? existingIndex,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken,
    string projectName = "") : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editPrompt = UseState("");
        UseEffect(() =>
        {
            var verifications = config.Settings.Verifications;
            if (existingIndex is >= 0 && existingIndex < verifications.Count)
            {
                editName.Set(verifications[existingIndex.Value].Name);
                editPrompt.Set(verifications[existingIndex.Value].Prompt);
            }
        }, EffectTrigger.OnMount());

        var verifications = config.Settings.Verifications;
        var isNew = existingIndex == null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(isNew ? "Add Verification" : "Edit Verification"),
            new DialogBody(
                Layout.Vertical()
                | editName.ToTextInput("Verification name...").WithField().Label("Name")
                | editPrompt.ToTextareaInput("Verification prompt...").Rows(8).WithField().Label("Prompt")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    var oldName = isNew ? null : verifications[existingIndex!.Value].Name;
                    var oldPrompt = isNew ? null : verifications[existingIndex!.Value].Prompt;
                    if (isNew)
                    {
                        verifications.Add(new VerificationConfig
                        {
                            Name = editName.Value,
                            Prompt = editPrompt.Value
                        });

                        if (!string.IsNullOrEmpty(projectName))
                        {
                            var proj = config.Settings.Projects
                                .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
                            if (proj != null && !proj.Verifications.Any(v => v.Name == editName.Value))
                                proj.Verifications.Add(new ProjectVerificationRef { Name = editName.Value, Required = true });
                        }
                    }
                    else
                    {
                        verifications[existingIndex!.Value].Name = editName.Value;
                        verifications[existingIndex!.Value].Prompt = editPrompt.Value;
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
                            verifications.RemoveAt(verifications.Count - 1);
                        else
                        {
                            verifications[existingIndex!.Value].Name = oldName!;
                            verifications[existingIndex!.Value].Prompt = oldPrompt!;
                        }
                        refreshToken.Refresh();
                        client.Toast($"Failed to save verification: {ex.Message}", "Error");
                    }
                })
            )
        ).Width(Size.Rem(35));
    }
}

using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Views;

namespace Ivy.Tendril.Apps.Setup.Dialogs;

public class AddProjectDialog(
    IState<bool> isOpen,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken) : ViewBase
{
    public override object? Build()
    {
        var step = UseState(0);
        var editName = UseState("");
        var editRepos = UseState(new List<RepoRef>());
        var isStepLoading = UseState(false);

        // State for step 2 (agent run)
        var verificationStream = UseStream<string>();
        var verificationHandle = UseState<PromptwareRunHandle?>();
        var verificationHasOutput = UseState(false);
        var verificationRunning = UseState(false);
        var verificationStarted = UseState(false);
        var verificationCancelled = UseState(false);
        var verificationError = UseState<string?>();
        var verificationRefreshToken = UseState(0);
        var hasCreated = UseState(false);
        var skipAgent = UseState(false);

        var session = new OnboardingVerificationSession(
            verificationStream,
            verificationHandle,
            verificationHasOutput,
            verificationRunning,
            verificationStarted,
            verificationCancelled,
            verificationError,
            verificationRefreshToken);

        UseEffect(() =>
        {
            if (step.Value >= 1)
            {
                hasCreated.Set(true);
            }
        }, step);

        if (!isOpen.Value) return null;

        void CancelAndClose()
        {
            session.Reset();

            if (hasCreated.Value && !string.IsNullOrWhiteSpace(editName.Value))
            {
                var project = config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(editName.Value, StringComparison.OrdinalIgnoreCase));
                if (project != null)
                {
                    config.Settings.Projects.Remove(project);
                    try
                    {
                        config.SaveSettings();
                    }
                    catch { }
                }
            }

            isOpen.Set(false);
            refreshToken.Refresh();
        }

        // Stepper steps
        var steps = new StepperItem[]
        {
            new("1", step.Value > 0 ? Icons.Check : null, "Details"),
            new("2", step.Value > 1 ? Icons.Check : null, "Analyze"),
            new("3", step.Value > 2 ? Icons.Check : null, "Configure")
        };

        var stepper = new Stepper((_) => ValueTask.CompletedTask, step.Value, steps).Width(Size.Full()).Disabled(true);

        object activeView = step.Value switch
        {
            0 => new ProjectInputStepView(
                editRepos,
                editName,
                isStepLoading,
                onBack: CancelAndClose,
                onNext: () => {
                    skipAgent.Set(false);
                    step.Set(1);
                },
                onSkip: () => {
                    skipAgent.Set(true);
                    step.Set(1);
                },
                skipButtonText: "Skip AI Setup"),
            1 => new ProjectAgentStepView(
                editRepos,
                editName,
                isStepLoading,
                session,
                onBack: () => {
                    session.Reset();
                    isStepLoading.Set(false);
                    step.Set(0);
                },
                onNext: () => {
                    step.Set(2);
                },
                onSkip: null,
                skipAgent: skipAgent.Value),
            2 => new ProjectCrudStepView(
                editName,
                isStepLoading,
                session,
                onBack: () => {
                    step.Set(0);
                    session.Reset();
                },
                onNext: () => {
                    hasCreated.Set(false); // Clear so we don't delete on close
                    isOpen.Set(false);
                    refreshToken.Refresh();
                    client.Toast($"Project '{editName.Value}' added successfully", "Success");
                },
                nextButtonText: "Finish"),
            _ => throw new ArgumentOutOfRangeException()
        };

        var dialogBody = Layout.Vertical().Gap(4)
            | stepper
            | new Separator()
            | activeView;

        return new Dialog(
            _ => CancelAndClose(),
            new DialogHeader("Add Project"),
            new DialogBody(dialogBody)
        ).Width(Size.Rem(40));
    }
}

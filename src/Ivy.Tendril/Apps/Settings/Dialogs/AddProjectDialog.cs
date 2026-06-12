using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Apps.Views;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

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

        UseEffect(() =>
        {
            if (isOpen.Value)
            {
                step.Set(0);
                editName.Set("");
                editRepos.Set(new List<RepoRef>());
                isStepLoading.Set(false);
                hasCreated.Set(false);
                skipAgent.Set(false);
                session.Reset();
            }
        }, isOpen);

        if (!isOpen.Value) return null;

        void RemoveCommittedProject()
        {
            if (!hasCreated.Value || string.IsNullOrWhiteSpace(editName.Value)) return;

            var project = config.Settings.Projects.FirstOrDefault(
                p => p.Name.Equals(editName.Value, StringComparison.OrdinalIgnoreCase));
            if (project != null)
            {
                config.Settings.Projects.Remove(project);
                try { config.SaveSettings(); } catch { }
            }

            hasCreated.Set(false);
        }

        void CancelAndClose()
        {
            session.Reset();
            RemoveCommittedProject();
            isOpen.Set(false);
            refreshToken.Refresh();
        }



        object activeView = step.Value switch
        {
            0 => new ProjectInputStepView(
                editRepos,
                editName,
                isStepLoading,
                onNext: () =>
                {
                    skipAgent.Set(false);
                    step.Set(1);
                },
                onSkip: () =>
                {
                    skipAgent.Set(true);
                    step.Set(1);
                },
                skipButtonText: "Manual Setup",
                nextButtonText: "Create Project",
                title: "Add a project",
                disableSkipWhenCannotContinue: true,
                showHeader: false),
            1 => new ProjectAgentStepView(
                editRepos,
                editName,
                isStepLoading,
                session,
                onBack: () =>
                {
                    session.Reset();
                    isStepLoading.Set(false);
                    RemoveCommittedProject();
                    step.Set(0);
                },
                onNext: () =>
                {
                    step.Set(2);
                },
                onSkip: null,
                skipAgent: skipAgent.Value,
                showHeader: false),
            2 => new ProjectCrudStepView(
                editName,
                isStepLoading,
                session,
                onBack: () =>
                {
                    RemoveCommittedProject();
                    step.Set(0);
                    session.Reset();
                },
                onNext: () =>
                {
                    hasCreated.Set(false); // Clear so we don't delete on close
                    isOpen.Set(false);
                    refreshToken.Refresh();
                    client.Toast($"Project '{editName.Value}' added successfully", "Success");
                },
                nextButtonText: "Finish",
                showHeader: false),
            _ => throw new ArgumentOutOfRangeException()
        };

        var dialogBody = Layout.Vertical()
            | activeView;

        var headerTitle = step.Value switch
        {
            0 => "Add a project",
            1 => "Setting up your project",
            2 => "Review Harness",
            _ => "Add Project"
        };

        return new Dialog(
            _ => CancelAndClose(),
            new DialogHeader(headerTitle),
            new DialogBody(dialogBody)
        ).Width(Size.Rem(40));
    }
}

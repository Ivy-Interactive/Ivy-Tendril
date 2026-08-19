using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Apps.Settings;

public class AddProjectDialog(
    IState<bool> isOpen,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken,
    Action<string>? onCreated = null) : ViewBase
{
    public override object? Build()
    {
        var jobService = UseService<IJobService>();
        var step = UseState(0);
        var editName = UseState("");
        var editRepos = UseState(new List<RepoRef>());
        var isStepLoading = UseState(false);

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
        var setupTriggered = UseState(false);

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
            if (setupTriggered.Value && !skipAgent.Value && step.Value == 0)
            {
                step.Set(1);
            }
        }, [setupTriggered, skipAgent]);

        UseEffect(() =>
        {
            if (skipAgent.Value && setupTriggered.Value && step.Value == 0 && !session.Running.Value && !isStepLoading.Value)
            {
                step.Set(2);
            }
        }, [skipAgent, setupTriggered, step, session.Running, isStepLoading]);

        // Reset state when dialog is closed or reopened
        UseEffect(() =>
        {
            if (!isOpen.Value)
            {
                step.Set(0);
                editName.Set("");
                editRepos.Set(new List<RepoRef>());
                hasCreated.Set(false);
                skipAgent.Set(false);
                setupTriggered.Set(false);
                isStepLoading.Set(false);
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
            refreshToken.Refresh();
            isOpen.Set(false);
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
                    setupTriggered.Set(true);
                },
                onSkip: () =>
                {
                    skipAgent.Set(true);
                    setupTriggered.Set(true);
                },
                onBgJob: () =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value) || editRepos.Value.Count == 0) return;

                    var newProj = new ProjectConfig
                    {
                        Name = editName.Value.Trim(),
                        Repos = new List<RepoRef>(editRepos.Value)
                    };
                    config.Settings.Projects.Add(newProj);
                    try { config.SaveSettings(); } catch { }

                    jobService?.StartJob(new AddProjectArgs(newProj.Name, newProj.Repos));

                    client?.Toast($"Created background job for project '{newProj.Name}'", "Job Started");
                    refreshToken.Refresh();
                    isOpen.Set(false);
                    onCreated?.Invoke(newProj.Name);
                },
                skipButtonText: "Manual Setup",
                nextButtonText: "Create Project",
                title: "Add a Project",
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
                    setupTriggered.Set(false);
                    step.Set(0);
                },
                onNext: () =>
                {
                    step.Set(2);
                },
                onSkip: () =>
                {
                    step.Set(2);
                },
                skipAgent: skipAgent.Value,
                showHeader: false,
                setupTrigger: setupTriggered),
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
                    hasCreated.Set(false);
                    refreshToken.Refresh();
                    client?.Toast($"Project '{editName.Value}' added successfully", "Success");
                    isOpen.Set(false);
                    onCreated?.Invoke(editName.Value);
                },
                nextButtonText: "Finish",
                showHeader: false),
            _ => throw new ArgumentOutOfRangeException()
        };

        var title = step.Value switch
        {
            0 => "Add New Project",
            1 => "Setting Up Project...",
            2 => "Review Project Configuration",
            _ => "Add New Project"
        };

        return new Dialog(
            _ => CancelAndClose(),
            new DialogHeader(title),
            new DialogBody(
                Layout.Vertical()
                | activeView
            )
        ).Width(Size.Units(150));
    }
}

using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Settings.Blades;

public class AddProjectBladeView(
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken) : ViewBase
{
    public override object? Build()
    {
        var bladeContext = UseContext<IBladeContext>();
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
            bladeContext.Pop(this);
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

                    jobService.StartJob(new AddProjectArgs(newProj.Name, newProj.Repos));

                    client.Toast($"Created background job for project '{newProj.Name}'", "Job Started");
                    refreshToken.Refresh();
                    bladeContext.Pop(this);
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
                    client.Toast($"Project '{editName.Value}' added successfully", "Success");
                    bladeContext.Pop(this);
                },
                nextButtonText: "Finish",
                showHeader: false),
            _ => throw new ArgumentOutOfRangeException()
        };

        return Layout.Vertical()
            | activeView
            | new Button("Cancel").Outline().OnClick(() => CancelAndClose());
    }
}

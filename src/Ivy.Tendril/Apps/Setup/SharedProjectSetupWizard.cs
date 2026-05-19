using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ivy.Tendril.Apps.Setup;

public class SharedProjectSetupWizard(Action onCancel, Func<Task> onFinish) : ViewBase
{
    public override object Build()
    {
        var step = UseState(0);
        var selectedRepos = UseState(new List<RepoRef>());
        var projectName = UseState("");
        var isStepLoading = UseState(false);

        var verificationStream = UseStream<string>();
        var verificationHandle = UseState<PromptwareRunHandle?>((PromptwareRunHandle?)null);
        var verificationHasOutput = UseState(false);
        var verificationRunning = UseState(false);
        var verificationStarted = UseState(false);
        var verificationCancelled = UseState(false);
        var verificationError = UseState<string?>((string?)null);
        var verificationRefreshToken = UseState(0);
        var session = new OnboardingVerificationSession(
            verificationStream,
            verificationHandle,
            verificationHasOutput,
            verificationRunning,
            verificationStarted,
            verificationCancelled,
            verificationError,
            verificationRefreshToken);

        if (step.Value == 0)
        {
            return new ProjectSetupStepView(
                stepperIndex: step,
                selectedRepos: selectedRepos,
                projectName: projectName,
                isStepLoading: isStepLoading,
                session: session,
                isOnboarding: false,
                onCancel: onCancel,
                onFinish: onFinish
            );
        }
        else
        {
            return new CompleteStepView(
                stepperIndex: step,
                selectedRepos: selectedRepos,
                projectName: projectName,
                isStepLoading: isStepLoading,
                session: session,
                isOnboarding: false,
                onFinish: onFinish
            );
        }
    }
}

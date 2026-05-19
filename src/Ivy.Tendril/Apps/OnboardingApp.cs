using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Apps.Onboarding.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

#if DEBUG
[App(title: "Onboarding", icon: Icons.Rocket, group: ["Debug"], isVisible: true, order: Constants.Onboarding)]
#else
[App(icon: Icons.Rocket, isVisible: false, order: Constants.Onboarding)]
#endif
public class OnboardingApp : ViewBase
{
    private static StepperItem[] GetSteps(int selectedIndex)
    {
        return
        [
            new("1", selectedIndex > 0 ? Icons.Check : null, "Coding Agent"),
            new("2", selectedIndex > 1 ? Icons.Check : null, "Data Storage"),
            new("3", selectedIndex > 2 ? Icons.Check : null, "Your First Project"),
            new("4", selectedIndex > 3 ? Icons.Check : null, "Complete")
        ];
    }

    private static object GetStepViews(
        IState<int> stepperIndex,
        IState<bool> commonChecksPassed,
        IState<bool> homeBootstrapped,
        IState<string?> completedAgentKey,
        IState<string> tendrilHomePath,
        IState<List<RepoRef>> selectedRepos,
        IState<string> projectName,
        IState<bool> isStepLoading,
        OnboardingVerificationSession session)
    {
        return stepperIndex.Value switch
        {
            0 => new CodingAgentStepView(stepperIndex, commonChecksPassed, completedAgentKey, isStepLoading),
            1 => new TendrilHomeStepView(stepperIndex, tendrilHomePath, homeBootstrapped, isStepLoading),
            2 => new ProjectSetupStepView(stepperIndex, selectedRepos, projectName, isStepLoading, session),
            3 => new CompleteStepView(stepperIndex, selectedRepos, projectName, isStepLoading, session),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public override object Build()
    {
        var stepperIndex = UseState(0);
        var commonChecksPassed = UseState(false);
        var homeBootstrapped = UseState(false);
        var completedAgentKey = UseState<string?>();
        var tendrilHomePath = UseState(() =>
            Environment.GetEnvironmentVariable("TENDRIL_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril"));
        var selectedRepos = UseState(() => new List<RepoRef>());
        var projectName = UseState("");
        var isStepLoading = UseState(false);

        var verificationStream = UseStream<string>();
        var verificationHandle = UseState<PromptwareRunHandle?>();
        var verificationHasOutput = UseState(false);
        var verificationRunning = UseState(false);
        var verificationStarted = UseState(false);
        var verificationCancelled = UseState(false);
        var verificationError = UseState<string?>();
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

        var steps = GetSteps(stepperIndex.Value);

        var header = Layout.Horizontal().AlignContent(Align.BottomLeft)
                     | new Image("/tendril/assets/Tendril.svg").Width(Size.Units(15)).Height(Size.Auto())
                     | Text.H2("Welcome to Ivy Tendril")
            ;
        
        return Layout.TopCenter() |
               (Layout.Vertical().Margin(0, 20).Width(150)
                | header
                | new Spacer().Height(Size.Units(2))
                | new Stepper(OnSelect, stepperIndex.Value, steps).Width(Size.Full()).Disabled(isStepLoading.Value || verificationRunning.Value)
                | new Spacer().Height(Size.Units(2))
                | GetStepViews(stepperIndex,
                               commonChecksPassed, homeBootstrapped, completedAgentKey,
                               tendrilHomePath, selectedRepos, projectName, isStepLoading, session)
               );

        ValueTask OnSelect(Event<Stepper, int> e)
        {
            if (isStepLoading.Value || verificationRunning.Value) return ValueTask.CompletedTask;
            if (e.Value < stepperIndex.Value || stepperIndex.Value != 3)
            {
                stepperIndex.Set(e.Value);
            }
            return ValueTask.CompletedTask;
        }
    }
}

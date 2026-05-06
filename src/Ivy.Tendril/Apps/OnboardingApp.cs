using Ivy.Tendril.Apps.Onboarding;

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
            new("2", selectedIndex > 1 ? Icons.Check : null, "Your first project"),
            new("3", selectedIndex > 2 ? Icons.Check : null, "Complete")
        ];
    }

    private static object GetStepViews(
        IState<int> stepperIndex,
        IState<string[]> ghOwners,
        IState<Dictionary<string, string[]>> ghReposByOwner,
        IState<bool> commonChecksPassed,
        IState<bool> homeBootstrapped,
        IState<bool> reposFetched,
        IState<string?> completedAgentKey)
    {
        return stepperIndex.Value switch
        {
            0 => new CodingAgentStepView(stepperIndex, ghOwners, ghReposByOwner,
                                         commonChecksPassed, homeBootstrapped, reposFetched, completedAgentKey),
            1 => new ProjectSetupStepView(stepperIndex, ghOwners, ghReposByOwner),
            2 => new CompleteStepView(stepperIndex),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public override object Build()
    {
        var stepperIndex = UseState(0);
        var ghOwners = UseState<string[]>(Array.Empty<string>);
        var ghReposByOwner = UseState<Dictionary<string, string[]>>(() => new Dictionary<string, string[]>());
        var commonChecksPassed = UseState(false);
        var homeBootstrapped = UseState(false);
        var reposFetched = UseState(false);
        var completedAgentKey = UseState<string?>((string?)null);
        var steps = GetSteps(stepperIndex.Value);

        return Layout.TopCenter() |
               (Layout.Vertical().Margin(0, 32, 0, 0).Width(150)
                | new Stepper(OnSelect, stepperIndex.Value, steps).Width(Size.Full())
                | GetStepViews(stepperIndex, ghOwners, ghReposByOwner,
                               commonChecksPassed, homeBootstrapped, reposFetched, completedAgentKey)
               );

        ValueTask OnSelect(Event<Stepper, int> e)
        {
            stepperIndex.Set(e.Value);
            return ValueTask.CompletedTask;
        }
    }
}

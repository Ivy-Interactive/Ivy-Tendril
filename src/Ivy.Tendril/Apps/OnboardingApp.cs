using Ivy.Tendril.Apps.Onboarding;
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
            new("2", selectedIndex > 1 ? Icons.Check : null, "Data Location"),
            new("3", selectedIndex > 2 ? Icons.Check : null, "Your first project"),
            new("4", selectedIndex > 3 ? Icons.Check : null, "Complete")
        ];
    }

    private static object GetStepViews(
        IState<int> stepperIndex,
        IState<string[]> ghOwners,
        IState<Dictionary<string, string[]>> ghReposByOwner,
        IState<bool> commonChecksPassed,
        IState<bool> homeBootstrapped,
        IState<bool> reposFetched,
        IState<string?> completedAgentKey,
        IState<string> tendrilHomePath,
        IState<List<RepoRef>> selectedRepos,
        IState<string> projectName)
    {
        return stepperIndex.Value switch
        {
            0 => new CodingAgentStepView(stepperIndex, ghOwners, ghReposByOwner,
                                         commonChecksPassed, reposFetched, completedAgentKey),
            1 => new TendrilHomeStepView(stepperIndex, tendrilHomePath, homeBootstrapped),
            2 => new ProjectSetupStepView(stepperIndex, ghOwners, ghReposByOwner, selectedRepos, projectName),
            3 => new CompleteStepView(stepperIndex, selectedRepos, projectName),
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
        var tendrilHomePath = UseState(() =>
            Environment.GetEnvironmentVariable("TENDRIL_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril"));
        var selectedRepos = UseState(() => new List<RepoRef>());
        var projectName = UseState("");
        var steps = GetSteps(stepperIndex.Value);

        return Layout.TopCenter() |
               (Layout.Vertical().Margin(0, 20).Width(150)
                | new Image("/tendril/assets/Tendril.svg").Width(Size.Units(20)).Height(Size.Auto())
                | new Stepper(OnSelect, stepperIndex.Value, steps).Width(Size.Full())
                | GetStepViews(stepperIndex, ghOwners, ghReposByOwner,
                               commonChecksPassed, homeBootstrapped, reposFetched, completedAgentKey,
                               tendrilHomePath, selectedRepos, projectName)
               );

        ValueTask OnSelect(Event<Stepper, int> e)
        {
            stepperIndex.Set(e.Value);
            return ValueTask.CompletedTask;
        }
    }
}

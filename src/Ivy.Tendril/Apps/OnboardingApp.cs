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
            new("2", selectedIndex > 1 ? Icons.Check : null, "Storage"),
            new("3", selectedIndex > 2 ? Icons.Check : null, "Your first project"),
            new("4", selectedIndex > 3 ? Icons.Check : null, "Complete")
        ];
    }

    private static object GetStepViews(IState<int> stepperIndex)
    {
        return stepperIndex.Value switch
        {
            0 => new CodingAgentStepView(stepperIndex),
            1 => new TendrilHomeStepView(stepperIndex),
            2 => new ProjectSetupStepView(stepperIndex),
            3 => new CompleteStepView(stepperIndex),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public override object Build()
    {
        var stepperIndex = UseState(0);
        var steps = GetSteps(stepperIndex.Value);

        return Layout.TopCenter() |
               (Layout.Vertical().Margin(0, 32, 0, 0).Width(150)
                | new Stepper(OnSelect, stepperIndex.Value, steps).Width(Size.Full())
                | GetStepViews(stepperIndex)
               );

        ValueTask OnSelect(Event<Stepper, int> e)
        {
            stepperIndex.Set(e.Value);
            return ValueTask.CompletedTask;
        }
    }
}

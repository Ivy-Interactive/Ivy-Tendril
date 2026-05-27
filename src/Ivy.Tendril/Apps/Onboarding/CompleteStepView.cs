using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public class CompleteStepView(
    IState<int> stepperIndex,
    IState<int> projectSubStep,
    IState<bool> isStepLoading) : ViewBase
{
    public override object Build()
    {
        var setupService = UseService<IOnboardingSetupService>();
        var client = UseService<IClientProvider>();

        var isFinishing = UseState(false);
        var error = UseState<string?>(null);

        async Task OnFinish()
        {
            isFinishing.Set(true);
            isStepLoading.Set(true);
            error.Set(null);
            try
            {
                await setupService.FinalizeOnboardingAsync();
                await setupService.StartBackgroundServicesAsync();
                client.ReloadPage();
            }
            catch (Exception ex)
            {
                error.Set($"Failed to complete setup: {ex.Message}");
                isFinishing.Set(false);
                isStepLoading.Set(false);
            }
        }

        void OnBack()
        {
            projectSubStep.Set(0);
            stepperIndex.Set(2);
        }

        var newsletter = new Box().Padding(4) | (Layout.Vertical().Gap(2)
                          | Text.H3("Newsletter")
                          | Text.Muted("Be the first to know when we have a new release!")
                          | new NewsletterView());

        return Layout.Vertical().Margin(0, 0, 0, 20)
               | Text.H3("Ready to Go!")
               | Text.Muted("Your project is configured. Click Finish to start using Tendril.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | newsletter
               | new Spacer().Height(Size.Units(4))
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                      .Disabled(isFinishing.Value)
                      .OnClick(OnBack)
                  | new Spacer()
                  | new Button("Finish").Primary().Large().Icon(Icons.Check, Align.Right)
                      .Disabled(isFinishing.Value)
                      .Loading(isFinishing.Value)
                      .OnClick(async () => await OnFinish()));
    }
}

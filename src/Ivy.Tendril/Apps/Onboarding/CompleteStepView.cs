using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Onboarding;

public class CompleteStepView(IState<int> stepperIndex) : ViewBase
{
    public override object Build()
    {
        var isProcessing = UseState(false);
        var error = UseState<string?>(null);
        var setupService = UseService<IOnboardingSetupService>();
        var client = UseService<IClientProvider>();

        async Task OnComplete()
        {
            isProcessing.Set(true);
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
                isProcessing.Set(false);
            }
        }

        return Layout.Vertical().Margin(0, 0, 0, 20)
               | Text.H2("Ready to Go!")
               | Text.Markdown("All set — click **Finish** to start using Tendril.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | (Layout.Horizontal().Width(Size.Full())
                  | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                      .Disabled(isProcessing.Value)
                      .OnClick(() => stepperIndex.Set(stepperIndex.Value - 1))
                  | new Spacer()
                  | new Button("Finish").Primary().Large().Icon(Icons.Check, Align.Right)
                      .Disabled(isProcessing.Value)
                      .Loading(isProcessing.Value)
                      .OnClick(async () => await OnComplete()));
    }
}

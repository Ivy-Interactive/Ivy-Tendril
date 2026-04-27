using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Onboarding;

public class CompleteStepView(IState<int> stepperIndex) : ViewBase
{
    public override object Build()
    {
        var isProcessing = UseState(false);
        var error = UseState<string?>(null);
        var config = UseService<IConfigService>();
        var setupService = UseService<IOnboardingSetupService>();
        var client = UseService<IClientProvider>();

        async Task OnComplete()
        {
            isProcessing.Set(true);
            error.Set(null);

            try
            {
                var tendrilHome = config.GetPendingTendrilHome();

                if (string.IsNullOrEmpty(tendrilHome))
                {
                    error.Set("Tendril home path not set");
                    isProcessing.Set(false);
                    return;
                }

                await setupService.CompleteSetupAsync(tendrilHome);

                // Fire-and-forget: services start in background (~100ms) and will be
                // ready before the browser finishes the full-page reload below.
                // Awaiting Task.Run shifts the continuation to a thread-pool thread,
                // which prevents client.Redirect from delivering the WebSocket message.
                _ = setupService.StartBackgroundServicesAsync();

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
               | Text.Markdown(
                   "We'll now:\n" +
                   "- Create the necessary folder structure\n" +
                   "- Set up your configuration file\n" +
                   "- Initialize Tendril with default settings\n\n" +
                   "Click 'Complete Setup' to finish.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | new Button("Complete Setup")
                   .Primary()
                   .Large()
                   .Icon(Icons.Check, Align.Right)
                   .Disabled(isProcessing.Value)
                   .Loading(isProcessing.Value)
                   .OnClick(async () => await OnComplete());
    }
}

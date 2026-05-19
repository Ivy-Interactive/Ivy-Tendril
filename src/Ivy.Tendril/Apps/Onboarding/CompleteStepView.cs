using System.Net.Http.Json;
using Ivy.Tendril.Helpers;
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
        var httpClientFactory = UseService<IHttpClientFactory>();
        var telemetry = UseService<ITelemetryService>();

        var isFinishing = UseState(false);
        var error = UseState<string?>(null);
        var newsletterEmail = UseState("");
        var newsletterSubscribed = UseState(false);
        var newsletterError = UseState<string?>(null);
        var newsletterLoading = UseState(false);

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

        async ValueTask Subscribe(Event<Button> e)
        {
            if (!InputSanitizer.IsValidEmail(newsletterEmail.Value))
            {
                newsletterError.Set("Please enter a valid email address.");
                return;
            }

            newsletterLoading.Set(true);
            try
            {
                using var http = httpClientFactory.CreateClient();
                var response = await http.PostAsJsonAsync("https://tendril-api.ivy.app/subscribers", new
                {
                    email = newsletterEmail.Value,
                    anonymousId = telemetry.AnonymousId
                });
                response.EnsureSuccessStatusCode();
                newsletterSubscribed.Set(true);
                newsletterError.Set(null);
            }
            catch
            {
                newsletterError.Set("Subscription failed. Please try again.");
            }
            finally
            {
                newsletterLoading.Set(false);
            }
        }

        void OnBack()
        {
            projectSubStep.Set(0);
            stepperIndex.Set(2);
        }

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H3("Ready to Go!")
               | Text.Muted("Your project is configured. Click Finish to start using Tendril.")
               | (error.Value != null ? Text.Danger(error.Value) : null!)
               | (Layout.Vertical().Gap(2)
                  | Text.H3("Newsletter")
                  | Text.Muted("Be the first to know when we have a new release!")
                  | (newsletterSubscribed.Value
                      ? Text.Success("Subscribed!")
                      : (Layout.Horizontal()
                         | newsletterEmail.ToTextInput("you@example.com")
                         | new Button("Subscribe")
                             .Primary()
                             .Disabled(!InputSanitizer.IsValidEmail(newsletterEmail.Value))
                             .Loading(newsletterLoading.Value)
                             .OnClick(Subscribe)))
                  | (newsletterError.Value != null ? Text.Danger(newsletterError.Value) : null!))
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

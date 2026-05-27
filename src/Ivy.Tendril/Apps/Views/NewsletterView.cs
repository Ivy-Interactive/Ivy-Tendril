using System.Net.Http.Json;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Views;

public class NewsletterView : ViewBase
{
    public override object Build()
    {
        var httpClientFactory = UseService<IHttpClientFactory>();
        var telemetry = UseService<ITelemetryService>();

        var email = UseState("");
        var subscribed = UseState(false);
        var error = UseState<string?>(null);
        var isLoading = UseState(false);

        async ValueTask Subscribe(Event<Button> e)
        {
            if (!InputSanitizer.IsValidEmail(email.Value))
            {
                error.Value = "Please enter a valid email address.";
                return;
            }

            isLoading.Value = true;
            try
            {
                using var http = httpClientFactory.CreateClient();
                var response = await http.PostAsJsonAsync("https://tendril-api.ivy.app/subscribers", new
                {
                    email = email.Value,
                    anonymousId = telemetry.AnonymousId,
                    source = "tendril"
                });

                if (!response.IsSuccessStatusCode)
                {
                    error.Value = response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.TooManyRequests => "Too many attempts. Please try again later.",
                        System.Net.HttpStatusCode.Conflict => "This email is already subscribed.",
                        _ => "Something went wrong. Please try again later."
                    };
                    return;
                }

                subscribed.Value = true;
                error.Value = null;
            }
            catch
            {
                error.Value = "Could not connect. Please check your internet connection.";
            }
            finally
            {
                isLoading.Value = false;
            }
        }

        return Layout.Vertical()
               | (subscribed.Value
                   ? Text.Success("Subscribed!")
                   : (Layout.Horizontal()
                      | email.ToTextInput("you@example.com")
                      | new Button("Subscribe")
                          .Primary()
                          .Disabled(!InputSanitizer.IsValidEmail(email.Value))
                          .Loading(isLoading.Value)
                          .OnClick(Subscribe)))
               | (error.Value != null ? Text.Danger(error.Value) : null);
    }
}

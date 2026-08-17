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

        void Subscribe(Event<Button> e)
        {
            if (!InputSanitizer.IsValidEmail(email.Value))
            {
                error.Value = "Please enter a valid email address.";
                return;
            }

            var submittedEmail = email.Value;
            var anonymousId = telemetry.AnonymousId;

            subscribed.Value = true;
            error.Value = null;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = httpClientFactory.CreateClient();
                    var response = await http.PostAsJsonAsync("https://tendril-api.ivy.app/subscribers", new
                    {
                        email = submittedEmail,
                        anonymousId = anonymousId,
                        source = "tendril"
                    });

                    if (!response.IsSuccessStatusCode)
                    {
                        subscribed.Value = false;
                        error.Value = response.StatusCode switch
                        {
                            System.Net.HttpStatusCode.TooManyRequests => "Too many attempts. Please try again later.",
                            System.Net.HttpStatusCode.Conflict => "This email is already subscribed.",
                            _ => "Something went wrong. Please try again later."
                        };
                    }
                }
                catch
                {
                    try
                    {
                        subscribed.Value = false;
                        error.Value = "Could not connect. Please check your internet connection.";
                    }
                    catch
                    {
                        // View may be disposed if user navigated away
                    }
                }
            });
        }

        return Layout.Vertical()
               | (subscribed.Value
                   ? Text.Success("Subscribed!")
                   : (Layout.Horizontal()
                      | email.ToTextInput("you@example.com")
                      | new Button("Subscribe")
                          .Primary()
                          .Disabled(!InputSanitizer.IsValidEmail(email.Value))
                          .OnClick(Subscribe)))
               | (error.Value != null ? Text.Danger(error.Value) : null);
    }
}

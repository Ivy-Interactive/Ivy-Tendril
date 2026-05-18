using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

[App(title: "Help", icon: Icons.CircleQuestionMark, group: ["Apps"], order: Constants.Help)]
public class HelpApp : ViewBase
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var httpClientFactory = UseService<IHttpClientFactory>();
        var telemetry = UseService<ITelemetryService>();
        var email = UseState("");
        var subscribed = UseState(false);
        var error = UseState<string?>(null);
        var isLoading = UseState(false);

        bool IsValidEmail(string emailAddress) =>
            !string.IsNullOrWhiteSpace(emailAddress) && EmailRegex.IsMatch(emailAddress);

        async ValueTask Subscribe(Event<Button> e)
        {
            if (!IsValidEmail(email.Value))
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
                    anonymousId = telemetry.AnonymousId
                });
                response.EnsureSuccessStatusCode();
                subscribed.Value = true;
                error.Value = null;
            }
            catch (Exception ex)
            {
                error.Value = ex.Message;
            }
            finally
            {
                isLoading.Value = false;
            }
        }

        return Layout.TopCenter()
               | (Layout.Vertical().Margin(0, 20).Width(150).Gap(3)
                  | Text.H1("Help")
                  | Text.Muted($"View documentation at {Constants.DocsUrl} or join us on Discord for help.")
                  | (Layout.Horizontal()
                     | new Button("Open Documentation")
                         .Primary()
                         .Large()
                         .Icon(Icons.ExternalLink, Align.Right)
                         .OnClick(() => client.OpenUrl(Constants.DocsUrl))
                     | new Button("Join Discord")
                         .Primary()
                         .Large()
                         .Icon(Icons.Discord, Align.Right)
                         .OnClick(() => client.OpenUrl(Constants.DiscordUrl)))
                  | new Separator()
                  | Text.Muted("Found a bug? Submit an issue on GitHub.")
                  | new Button("Submit Issue")
                      .Secondary()
                      .Large()
                      .Icon(Icons.Bug, Align.Right)
                      .OnClick(() => client.OpenUrl(Constants.IssuesUrl))
                  | new Separator()
                  | Text.Muted("Subscribe to our newsletter for updates and tips.")
                  | (subscribed.Value
                      ? Text.Success("Subscribed!")
                      : (Layout.Horizontal()
                         | email.ToTextInput("you@example.com")
                         | new Button("Subscribe")
                             .Primary()
                             .Disabled(!IsValidEmail(email.Value))
                             .Loading(isLoading.Value)
                             .OnClick(Subscribe)))
                  | (error.Value != null ? Text.Danger(error.Value) : null)
               );
    }
}



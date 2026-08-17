using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class HelpSetupView : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();

        return Layout.Vertical().Width(Size.Auto().Max(Size.Units(120)))
               | Text.Block("Help & Support").Bold()
               | Text.Block($"View documentation at {Constants.DocsUrl} or join us on Discord for help.").Muted().Small()
               | (Layout.Horizontal()
                  | new Button("Open Documentation")
                      .Primary()
                      .Icon(Icons.ExternalLink, Align.Right)
                      .OnClick(() => client.OpenUrl(Constants.DocsUrl))
                  | new Button("Join Discord")
                      .Primary()
                      .Icon(Icons.Discord, Align.Right)
                      .OnClick(() => client.OpenUrl(Constants.DiscordUrl)))
               | new Separator()
               | Text.Block("Bugs or Ideas?").Bold()
               | Text.Block("Submit an issue on GitHub.").Muted().Small()
               | (Layout.Horizontal()
                  | new Button("Submit Issue")
                      .Primary()
                      .Icon(Icons.Bug, Align.Right)
                      .OnClick(() => client.OpenUrl(Constants.IssuesUrl)))
               | new Separator()
               | Text.Block("Newsletter").Bold()
               | Text.Block("Be the first to know when we have a new release!").Muted().Small()
               | new NewsletterView();
    }
}

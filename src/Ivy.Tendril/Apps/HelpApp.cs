using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

[App(title: "Help", icon: Icons.CircleQuestionMark, group: ["Apps"], order: Constants.Help)]
public class HelpApp : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();

        return Layout.TopCenter()
               | (Layout.Vertical().Margin(0, 20)
                  .Width(Size.Full().At(Breakpoint.Mobile).And(Breakpoint.Desktop, Size.Units(150)))
                  .Padding(new Responsive<Thickness?> { Mobile = new Thickness(4, 0, 4, 0) })
                  | Text.H1("Help")
                  | Text.Muted($"View documentation at {Constants.DocsUrl} or join us on Discord for help.")
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
                  | Text.H2("Bugs or Ideas?")
                  | Text.Muted("Submit an issue on GitHub.")
                  | new Button("Submit Issue")
                      .Secondary()
                      .Icon(Icons.Bug, Align.Right)
                      .OnClick(() => client.OpenUrl(Constants.IssuesUrl))
                  | new Separator()
                  | Text.H2("Newsletter")
                  | Text.Muted("Be the first to know when we have a new release!")
                  | new NewsletterView()
               );
    }
}

namespace Ivy.Tendril.Apps;

[App(title: "Help", icon: Icons.CircleQuestionMark, group: ["Apps"], order: Constants.Help)]
public class HelpApp : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();

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
               );
    }
}


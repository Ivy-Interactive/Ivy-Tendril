using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;
using WebViewerWidget = Ivy.Tendril.Widgets.WebViewer;

namespace WidgetSamples.Apps.WebViewer;

// The chrome exactly as Tendril's review action uses it: the widget's own toolbar, plus one
// host action - Update - that appears once there is a comment to send and carries the count.
// Press Select, pick an element, type a comment, and the badge appears; Update lists what
// would be sent and clears the pins.
[App(title: "Review preview", icon: Icons.MessageSquare, group: ["WebViewer"])]
public class ReviewPreviewApp : ViewBase
{
    private const string UpdateActionId = "update";

    public record Comment(string Id, int Number, string Tag, string Selector, string Text, string Page);

    public override object Build()
    {
        var site = UseService<DemoSiteAddress>();
        var commands = UseStream<WebViewerCommand>();
        var url = UseState(site.Home);
        var device = UseState(WebViewerDevice.Desktop);
        var comments = UseState(ImmutableList<Comment>.Empty);

        var (dialog, showDialog) = UseTrigger(open => new SendCommentsDialog(open, comments, () =>
        {
            commands.Write(new ClearCommentsCommand());
            comments.Set(ImmutableList<Comment>.Empty);
        }));

        var count = comments.Value.Count;
        var actions = count == 0
            ? Array.Empty<WebViewerAction>()
            :
            [
                new WebViewerAction(UpdateActionId, WebViewerIcon.MessageSquare,
                    $"Send {count} comment{(count == 1 ? "" : "s")} to the agent as a change request")
                {
                    Badge = count.ToString(),
                    Primary = true,
                },
            ];

        var viewer = new WebViewerWidget()
            .Url(url.Value)
            .Device(device.Value)
            .Commands(commands)
            .Toolbar()
            .Actions(actions)
            .WithOnEvent(e =>
            {
                switch (e)
                {
                    case NavigateEvent nav:
                        url.Set(nav.Url);
                        break;
                    case DeviceChangedEvent changed:
                        device.Set(changed.Device);
                        break;
                    case ActionEvent { Id: UpdateActionId }:
                        showDialog();
                        break;
                    case CommentEvent c:
                        comments.Set(prev => prev.Add(new Comment(c.Id, c.Number, c.Tag, c.Selector, c.Comment, c.Url ?? "")));
                        break;
                    case CommentUpdatedEvent u:
                        comments.Set(prev => prev
                            .Select(x => x.Id == u.Id ? x with { Text = u.Comment } : x)
                            .ToImmutableList());
                        break;
                    case CommentDeletedEvent d:
                        comments.Set(prev => prev
                            .RemoveAll(x => x.Id == d.Id)
                            .Select((x, i) => x with { Number = i + 1 })
                            .ToImmutableList());
                        break;
                }
            })
            .Width(Size.Full())
            .Height(Size.Full());

        return new Fragment(viewer.WithLayout().Full().RemoveParentPadding(), dialog);
    }
}

public class SendCommentsDialog(
    IState<bool> dialogOpen,
    IState<ImmutableList<ReviewPreviewApp.Comment>> comments,
    Action onSubmitted) : ViewBase
{
    public override object? Build()
    {
        var client = UseService<IClientProvider>();
        if (!dialogOpen.Value) return null;

        var pending = comments.Value;
        var pages = pending
            .GroupBy(c => c.Page)
            .Select(page => (object)(Layout.Vertical().Gap(1)
                | Text.Strong(page.Key)
                | (Layout.Vertical().Gap(2) | page.Select(c => (object)(Layout.Vertical().Gap(0)
                    | Text.Block($"{c.Number}. {c.Text}")
                    | Text.Muted($"{(string.IsNullOrEmpty(c.Tag) ? "element" : c.Tag)} · {c.Selector}"))))));

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Update Plan #42"),
            new DialogBody(
                Layout.Vertical().Gap(2)
                | Text.P($"{pending.Count} comment(s) from the running app will be sent to the agent as a change request.")
                | (Layout.Vertical().Gap(2) | pages)
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button("Update").ShortcutKey("Enter").AutoFocus().OnClick(() =>
                {
                    onSubmitted();
                    dialogOpen.Set(false);
                    client.Toast($"{pending.Count} comment(s) sent. The pins are cleared.", "Change request queued");
                })
            )
        ).Width(Size.Rem(32));
    }
}

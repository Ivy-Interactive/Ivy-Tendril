using System.Collections.Immutable;
using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;
using WebViewerWidget = Ivy.Tendril.Widgets.WebViewer;

namespace Ivy.Tendril.Apps.ReviewAction;

/// <summary>
///     What a review action turns into once the app it started has printed its URL: that app,
///     framed and proxied, behind the widget's own browser chrome. Back, forward, reload, the
///     address bar, the element picker and the viewport menu are the widget's; this view adds
///     one action of its own, Update, which appears once there is a comment to send.
///
///     Deliberately not the WebViewer sample's inspector: no console, network or capture
///     panels, because the point here is review, and every panel is a thing between the
///     reviewer and the app. What the widget reports beyond comments, navigation and its own
///     toolbar is dropped on the floor.
///
///     Comments are the output. Each one is pinned to its element in the page and carries where
///     that element came from in the source when the widget could resolve it; Update hands the
///     lot to the agent as a change request (see <see cref="UpdateFromCommentsDialog"/>).
/// </summary>
public class AppPreviewView(PlanFile plan, string appUrl) : ViewBase
{
    private const string UpdateActionId = "update";

    public override object Build()
    {
        var jobService = UseService<IJobService>();
        var planService = UseService<IPlanReaderService>();
        var commands = UseStream<WebViewerCommand>();
        var url = UseState(appUrl);
        var device = UseState(WebViewerDevice.Desktop);
        var comments = UseState(ImmutableList<AppComment>.Empty);

        var (updateDialog, showUpdateDialog) = UseTrigger(open => new UpdateFromCommentsDialog(
            open,
            plan,
            appUrl,
            comments,
            jobService,
            planService,
            // The widget owns the pins, so clearing our list is only half of it: without this
            // the page stays marked up with feedback that has already been sent.
            onSubmitted: () =>
            {
                commands.Write(new ClearCommentsCommand());
                comments.Set(ImmutableList<AppComment>.Empty);
            }));

        var viewer = new WebViewerWidget()
            .Url(url.Value)
            .Device(device.Value)
            .Commands(commands)
            .Toolbar()
            .Actions(UpdateActions(comments.Value.Count))
            .WithOnEvent(e =>
            {
                switch (e)
                {
                    case NavigateEvent nav:
                        // Written back into Url, which the widget ignores when it matches the
                        // page it is already showing - so this cannot loop.
                        url.Set(nav.Url);
                        break;

                    case DeviceChangedEvent changed:
                        device.Set(changed.Device);
                        break;

                    case ActionEvent { Id: UpdateActionId }:
                        showUpdateDialog();
                        break;

                    case CommentEvent c:
                        comments.Set(prev => prev.Add(
                            new AppComment(c.Id, c.Number, c.Tag, c.Selector, c.Comment, c.DebugJson, c.Url,
                                c.Text, c.AttrsJson, c.Device)));
                        break;

                    case CommentUpdatedEvent u:
                        comments.Set(prev => prev
                            .Select(x => x.Id == u.Id ? x with { Comment = u.Comment } : x)
                            .ToImmutableList());
                        break;

                    case CommentDeletedEvent d:
                        // A number is a position, so what is left closes ranks - the same thing
                        // the pins in the page do.
                        comments.Set(prev => prev
                            .RemoveAll(x => x.Id == d.Id)
                            .Select((x, i) => x with { Number = i + 1 })
                            .ToImmutableList());
                        break;
                }
            })
            .Width(Size.Full())
            .Height(Size.Full());

        return new Fragment(
            viewer.WithLayout().Full().RemoveParentPadding(),
            updateDialog);
    }

    // Only once there is something to send: an Update button with nothing behind it invites
    // a change request made of nothing.
    private static WebViewerAction[] UpdateActions(int commentCount) =>
        commentCount == 0
            ? []
            :
            [
                new WebViewerAction(
                    UpdateActionId,
                    WebViewerIcon.MessageSquare,
                    $"Send {commentCount} comment{(commentCount == 1 ? "" : "s")} to the agent as a change request")
                {
                    Badge = commentCount.ToString(),
                    Primary = true,
                },
            ];
}

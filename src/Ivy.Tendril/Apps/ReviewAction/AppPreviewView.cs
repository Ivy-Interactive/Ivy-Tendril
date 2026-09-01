using System.Collections.Immutable;
using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;
using WebViewerWidget = Ivy.Tendril.Widgets.WebViewer;

namespace Ivy.Tendril.Apps.ReviewAction;

/// <summary>
///     What a review action turns into once the app it started has printed its URL: that app,
///     framed and proxied, with just enough chrome to walk through it and mark up what is wrong.
///
///     Deliberately not a browser and not the WebViewer sample's inspector — no console,
///     network or capture panels, because the point here is review, and every panel is a thing
///     between the reviewer and the app. What the widget reports beyond comments and navigation
///     is dropped on the floor.
///
///     Comments are the output. Each one is pinned to its element in the page and carries where
///     that element came from in the source when the widget could resolve it; "Update" hands the
///     lot to the agent as a change request (see <see cref="UpdateFromCommentsDialog"/>).
/// </summary>
public class AppPreviewView(PlanFile plan, string appUrl) : ViewBase
{
    public override object Build()
    {
        var jobService = UseService<IJobService>();
        var commands = UseStream<WebViewerCommand>();
        var address = UseState(appUrl);      // the editable location bar
        var url = UseState(appUrl);          // what the viewer is pointed at
        var device = UseState(WebViewerDevice.Desktop);
        var canGoBack = UseState(false);
        var canGoForward = UseState(false);
        var selecting = UseState(false);
        var comments = UseState(ImmutableList<AppComment>.Empty);

        var (updateDialog, showUpdateDialog) = UseTrigger(open => new UpdateFromCommentsDialog(
            open,
            plan,
            appUrl,
            comments,
            jobService,
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
            .WithOnEvent(e =>
            {
                switch (e)
                {
                    case NavigateEvent nav:
                        // Also written back into Url, which the widget ignores when it matches
                        // the page it is already showing — so this cannot loop.
                        address.Set(nav.Url);
                        url.Set(nav.Url);
                        canGoBack.Set(nav.CanGoBack);
                        canGoForward.Set(nav.CanGoForward);
                        break;

                    case CommentEvent c:
                        comments.Set(prev => prev.Add(
                            new AppComment(c.Id, c.Number, c.Tag, c.Selector, c.Comment, c.DebugJson)));
                        selecting.Set(false); // the page leaves select mode after a pick
                        break;

                    case CommentUpdatedEvent u:
                        comments.Set(prev => prev
                            .Select(x => x.Id == u.Id ? x with { Comment = u.Comment } : x)
                            .ToImmutableList());
                        break;

                    case CommentDeletedEvent d:
                        // A number is a position, so what is left closes ranks — the same thing
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

        Button DeviceButton(string label, Icons icon, WebViewerDevice value)
        {
            var button = new Button(label).Icon(icon).Small().Tooltip(label)
                .OnClick(() => device.Set(value));
            return device.Value == value ? button.Primary() : button.Outline();
        }

        void Navigate() => url.Set(address.Value);

        var selectButton = new Button(selecting.Value ? "Selecting…" : "Select")
            .Icon(Icons.SquareDashedMousePointer)
            .Small()
            .Tooltip("Pick an element to comment on")
            .OnClick(() =>
            {
                var next = !selecting.Value;
                selecting.Set(next);
                commands.Write(new SelectModeCommand(next));
            });
        selectButton = selecting.Value ? selectButton.Primary() : selectButton.Outline();

        var toolbar = Layout.Horizontal().Gap(1).Width(Size.Full())
            | new Button("").Icon(Icons.ArrowLeft).Small().Outline().Tooltip("Back")
                .Disabled(!canGoBack.Value).OnClick(() => commands.Write(new BackCommand()))
            | new Button("").Icon(Icons.ArrowRight).Small().Outline().Tooltip("Forward")
                .Disabled(!canGoForward.Value).OnClick(() => commands.Write(new ForwardCommand()))
            | new Button("").Icon(Icons.RefreshCw).Small().Outline().Tooltip("Reload")
                .OnClick(() => commands.Write(new ReloadCommand()))
            | address.ToTextInput().Placeholder("Enter a URL").Width(Size.Full()).OnSubmit(_ => Navigate())
            | new Button("Go").Small().Outline().OnClick(Navigate)
            | DeviceButton("Desktop", Icons.Monitor, WebViewerDevice.Desktop)
            | DeviceButton("Tablet", Icons.Tablet, WebViewerDevice.Tablet)
            | DeviceButton("Mobile", Icons.Smartphone, WebViewerDevice.Mobile)
            | selectButton;

        // Only once there is something to send: an Update button with nothing behind it invites
        // a change request made of nothing.
        if (!comments.Value.IsEmpty)
        {
            toolbar |= new Button("Update")
                .Icon(Icons.MessageSquare)
                .Small()
                .Primary()
                .Badge(comments.Value.Count.ToString())
                .Tooltip("Send these comments to the agent as a change request")
                .OnClick(showUpdateDialog);
        }

        return new Fragment(
            new HeaderLayout(toolbar.Padding(2, 2, 1, 2), viewer)
                .Scroll(Scroll.None)
                .WithLayout()
                .Full()
                .RemoveParentPadding(),
            updateDialog);
    }
}

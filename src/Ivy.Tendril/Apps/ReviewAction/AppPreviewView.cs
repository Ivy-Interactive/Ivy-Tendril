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
        var planService = UseService<IPlanReaderService>();
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
                            new AppComment(c.Id, c.Number, c.Tag, c.Selector, c.Comment, c.DebugJson, c.Url,
                                c.Text, c.AttrsJson, c.Device)));
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

        void Navigate() => url.Set(address.Value);

        var selectButton = new Button(selecting.Value ? "Selecting…" : "Select")
            .Icon(Icons.SquareDashedMousePointer)
            .Tooltip("Pick an element to comment on")
            .OnClick(() =>
            {
                var next = !selecting.Value;
                selecting.Set(next);
                commands.Write(new SelectModeCommand(next));
            });
        selectButton = selecting.Value ? selectButton.Primary() : selectButton.Outline();

        // Icon-only, which is what keeps the toggle on one line: given labels it is three words
        // wide, and beside a full-width location bar it had nowhere to go but to wrap into a
        // block twice the height of the toolbar. An option with no label renders as its icon
        // alone, and the enum's own name still reaches the accessible name.
        var deviceOptions = new[]
        {
            new Option<WebViewerDevice>("", WebViewerDevice.Desktop) { Icon = Icons.Monitor, Tooltip = "Desktop" },
            // TabletSmartphone rather than Tablet: lucide's Tablet and Smartphone are both plain
            // rounded rectangles differing only in aspect ratio, which is a distinction that does
            // not survive being drawn at icon size. This one has its own silhouette.
            new Option<WebViewerDevice>("", WebViewerDevice.Tablet) { Icon = Icons.TabletSmartphone, Tooltip = "Tablet" },
            new Option<WebViewerDevice>("", WebViewerDevice.Mobile) { Icon = Icons.Smartphone, Tooltip = "Mobile" },
        };

        // No .Small() on any of these: small buttons sit shorter than a default-size text input,
        // so the row read as a line of little buttons floating against a taller location bar.
        var toolbar = Layout.Horizontal().Gap(1).Width(Size.Full())
            | new Button("").Icon(Icons.ArrowLeft).Outline().Tooltip("Back")
                .Disabled(!canGoBack.Value).OnClick(() => commands.Write(new BackCommand()))
            | new Button("").Icon(Icons.ArrowRight).Outline().Tooltip("Forward")
                .Disabled(!canGoForward.Value).OnClick(() => commands.Write(new ForwardCommand()))
            | new Button("").Icon(Icons.RefreshCw).Outline().Tooltip("Reload")
                .OnClick(() => commands.Write(new ReloadCommand()))
            // Grows, but only so far. On Full alone it swallowed every spare pixel in the row,
            // which is both more address bar than any URL needs and what squeezed the toggle
            // beside it into two rows.
            | address.ToTextInput().Placeholder("Enter a URL")
                .Width(Size.Full().Max(Size.Rem(40))).OnSubmit(_ => Navigate())
            | new Button("Go").Outline().OnClick(Navigate)
            // Fit, so the group is as wide as its three icons and has no reason to wrap however
            // the rest of the row is squeezed.
            | device.ToSelectInput(deviceOptions).Variant(SelectInputVariant.Toggle).Width(Size.Fit())
            | selectButton;

        // Only once there is something to send: an Update button with nothing behind it invites
        // a change request made of nothing.
        if (!comments.Value.IsEmpty)
        {
            toolbar |= new Button("Update")
                .Icon(Icons.MessageSquare)
                .Primary()
                .Badge(comments.Value.Count.ToString())
                .Tooltip("Send these comments to the agent as a change request")
                .OnClick(showUpdateDialog);
        }

        // Trailing room after Select and Update, which otherwise sit flush against the edge of
        // the panel with nothing between them and the frame below.
        toolbar |= new Spacer().Width(Size.Units(1));

        return new Fragment(
            new HeaderLayout(toolbar.Padding(2, 2, 1, 2), viewer)
                .Scroll(Scroll.None)
                .WithLayout()
                .Full()
                .RemoveParentPadding(),
            updateDialog);
    }
}

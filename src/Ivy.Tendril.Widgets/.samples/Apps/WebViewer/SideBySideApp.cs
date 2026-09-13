using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;
using WebViewerWidget = Ivy.Tendril.Widgets.WebViewer;

namespace WidgetSamples.Apps.WebViewer;

// Two WebViewers on one Ivy page, each with its own chrome - the case every per-viewer
// mechanism in the widget exists for. Both frames sit on the app's own origin and are
// therefore served by ONE service worker, so without the viewer token that each frame carries
// in its URL they would share a single emulated device (whichever viewer set it last), and
// every network entry and page message would be reported by both.
//
// What to look for:
//
//   - the left pane starts on the desktop layout and the right one on the phone layout, and
//     each toolbar's viewport menu changes only its own pane;
//   - navigating in one pane leaves the other's address bar and history alone;
//   - the request counters move independently;
//   - Select on one pane pins a numbered comment in that pane only. Click the pin to edit or
//     delete it; the strip above each pane is driven purely by the events Ivy receives.
[App(title: "Side by side", icon: Icons.Columns2, group: ["WebViewer"])]
public class SideBySideApp : ViewBase
{
    private record Pin(string Id, int Number, string Comment);

    public override object Build()
    {
        var site = UseService<DemoSiteAddress>();

        var leftDevice = UseState(WebViewerDevice.Desktop);
        var leftPins = UseState(ImmutableList<Pin>.Empty);
        var leftHttp = UseState(0);
        var leftConsole = UseState(0);

        var rightDevice = UseState(WebViewerDevice.Mobile);
        var rightPins = UseState(ImmutableList<Pin>.Empty);
        var rightHttp = UseState(0);
        var rightConsole = UseState(0);

        // One pane: the viewer plus the little strip that proves the events landed here and
        // not next door. Everything it needs is passed in, so the two panes share no state.
        object Pane(
            string title,
            IState<WebViewerDevice> device,
            IState<ImmutableList<Pin>> pins,
            IState<int> http,
            IState<int> console)
        {
            var viewer = new WebViewerWidget()
                .Url(site.Home)
                .Device(device.Value)
                .Toolbar()
                .WithOnEvent(e =>
                {
                    switch (e)
                    {
                        case DeviceChangedEvent changed:
                            device.Set(changed.Device);
                            break;
                        case HttpEvent:
                            http.Set(v => v + 1);
                            break;
                        case ConsoleEvent:
                            console.Set(v => v + 1);
                            break;
                        case CommentEvent c:
                            pins.Set(prev => prev.Add(new Pin(c.Id, c.Number, c.Comment)));
                            break;
                        case CommentUpdatedEvent u:
                            pins.Set(prev => prev
                                .Select(p => p.Id == u.Id ? p with { Comment = u.Comment } : p)
                                .ToImmutableList());
                            break;
                        case CommentDeletedEvent d:
                            // A pin's number is its position, so the survivors close ranks -
                            // exactly what the widget does to the pins in the page.
                            pins.Set(prev => prev
                                .RemoveAll(p => p.Id == d.Id)
                                .Select((p, i) => p with { Number = i + 1 })
                                .ToImmutableList());
                            break;
                    }
                })
                .Width(Size.Full())
                .Height(Size.Full());

            var comments = pins.Value.IsEmpty
                ? "no comments"
                : string.Join(" · ", pins.Value.Select(p => $"{p.Number}. {p.Comment}"));
            var strip = Text.Muted(
                $"{title} · {device.Value} · {http.Value} requests · {console.Value} console · {comments}");

            return new HeaderLayout(strip, viewer).Height(Size.Full()).Scroll(Scroll.None);
        }

        return new ResizablePanelGroup(
            new ResizablePanel(Size.Fraction(0.5f), Pane("Left", leftDevice, leftPins, leftHttp, leftConsole)),
            new ResizablePanel(Size.Fraction(0.5f), Pane("Right", rightDevice, rightPins, rightHttp, rightConsole))
        ).Height(Size.Full());
    }
}

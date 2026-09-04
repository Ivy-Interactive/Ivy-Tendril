using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;
using WebViewerWidget = Ivy.Tendril.Widgets.WebViewer;

namespace WidgetSamples.Apps.WebViewer;

// Two WebViewers on one Ivy page — the case every per-viewer mechanism in the widget exists
// for. Both frames sit on the app's own origin and are therefore served by ONE service
// worker, so without the viewer token that each frame carries in its URL they would share a
// single emulated device (whichever viewer set it last), and every network entry and page
// message would be reported by both.
//
// What to look for, with both panes on the same URL:
//
//   - the left pane renders the desktop layout and the right one the phone layout, and they
//     stay that way through a link click, which navigates inside its own pane;
//   - the request counters move independently — clicking a link on the left leaves the
//     right-hand count alone;
//   - Select on one pane pins a numbered comment in that pane only. Click the pin to edit or
//     delete it; the list under the toolbar is driven purely by the events Ivy receives.
[App(title: "Side by side", icon: Icons.Globe, group: ["WebViewer"])]
public class SideBySideApp : ViewBase
{
    private record Pin(string Id, int Number, string Comment);

    public override object Build()
    {
        const string home = "https://ivy.app";

        var address = UseState(home);
        var url = UseState(home);

        var leftCommands = UseStream<WebViewerCommand>();
        var leftPins = UseState(ImmutableList<Pin>.Empty);
        var leftHttp = UseState(0);
        var leftConsole = UseState(0);
        var leftSelecting = UseState(false);

        var rightCommands = UseStream<WebViewerCommand>();
        var rightPins = UseState(ImmutableList<Pin>.Empty);
        var rightHttp = UseState(0);
        var rightConsole = UseState(0);
        var rightSelecting = UseState(false);

        // One pane: the viewer plus the little strip that proves the events landed here and
        // not next door. Everything it needs is passed in, so the two panes share no state.
        object Pane(
            string title,
            WebViewerDevice device,
            IWriteStream<WebViewerCommand> commands,
            IState<ImmutableList<Pin>> pins,
            IState<int> http,
            IState<int> console,
            IState<bool> selecting)
        {
            var viewer = new WebViewerWidget()
                .Url(url.Value)
                .Device(device)
                .Commands(commands)
                .WithOnEvent(e =>
                {
                    switch (e)
                    {
                        case HttpEvent:
                            http.Set(v => v + 1);
                            break;
                        case ConsoleEvent:
                            console.Set(v => v + 1);
                            break;
                        case CommentEvent c:
                            pins.Set(prev => prev.Add(new Pin(c.Id, c.Number, c.Comment)));
                            selecting.Set(false); // the agent leaves select mode after a pick
                            break;
                        case CommentUpdatedEvent u:
                            pins.Set(prev => prev
                                .Select(p => p.Id == u.Id ? p with { Comment = u.Comment } : p)
                                .ToImmutableList());
                            break;
                        case CommentDeletedEvent d:
                            // A pin's number is its position, so the survivors close ranks —
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

            var select = new Button(selecting.Value ? "Selecting…" : "Select").Small()
                .OnClick(() =>
                {
                    var next = !selecting.Value;
                    selecting.Set(next);
                    commands.Write(new SelectModeCommand(next));
                });
            if (selecting.Value) select = select.Primary();

            var header = Layout.Horizontal().Gap(2).Width(Size.Full())
                | Text.Block(title)
                | select
                | new Button("⟳").Small().OnClick(() => commands.Write(new ReloadCommand()))
                | Text.Muted($"{http.Value} requests · {console.Value} console")
                | Text.Muted(pins.Value.IsEmpty
                    ? "no comments"
                    : string.Join(" · ", pins.Value.Select(p => $"{p.Number}. {p.Comment}")));

            return new HeaderLayout(header, viewer).Height(Size.Full()).Scroll(Scroll.None);
        }

        var toolbar = Layout.Horizontal().Gap(1).Width(Size.Full())
            | address.ToTextInput().Placeholder("Enter a URL").Width(Size.Full())
                .OnSubmit(_ => url.Set(address.Value))
            | new Button("Go").Small().Primary().OnClick(() => url.Set(address.Value));

        return new HeaderLayout(
            toolbar,
            new ResizablePanelGroup(
                new ResizablePanel(Size.Fraction(0.5f),
                    Pane("Desktop", WebViewerDevice.Desktop, leftCommands, leftPins, leftHttp, leftConsole, leftSelecting)),
                new ResizablePanel(Size.Fraction(0.5f),
                    Pane("Mobile", WebViewerDevice.Mobile, rightCommands, rightPins, rightHttp, rightConsole, rightSelecting))
            )
        ).Scroll(Scroll.None);
    }
}

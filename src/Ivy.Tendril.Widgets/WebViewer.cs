using System.Text.Json.Serialization;

namespace Ivy.Tendril.Widgets;

/// <summary>Viewport device-emulation profile.</summary>
public enum WebViewerDevice
{
    Desktop,
    Mobile,
    Tablet
}

/// <summary>
/// A thin viewport widget that loads any URL into a proxied sandbox iframe and surfaces
/// everything (console, clicks, comments, network, navigation, screenshots) through a
/// single typed <see cref="OnEvent"/> firehose. All UI (toolbar, DevTools panels) is
/// meant to be built in Ivy code; the widget owns only the iframe, the comment overlay
/// and the numbered pins that mark commented elements.
///
/// <para>The endpoints it depends on ship in this library, in
/// <see cref="WebViewerProxy"/>, and must be hosted by the Ivy app on the same origin:</para>
/// <code>
/// server.ReservePaths(WebViewerProxy.ReservedPaths);
/// server.UseWebApplication(app => app.MapWebViewerProxy());
/// </code>
///
/// <para>Several viewers may be mounted on one page; each keeps its own device emulation,
/// network log and comment pins.</para>
/// </summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "WebViewer",
    GlobalName = "IvyTendrilWidgets"
)]
public record WebViewer : WidgetBase<WebViewer>
{
    /// <summary>URL to load. Changing it navigates the iframe (the widget ignores changes
    /// that match the page it is already showing, so syncing this from OnEvent is safe).</summary>
    [Prop] public string? Url { get; init; }

    /// <summary>Device-emulation profile applied to the viewport + upstream User-Agent.</summary>
    [Prop] public WebViewerDevice Device { get; init; } = WebViewerDevice.Desktop;

    /// <summary>Typed imperative command stream (reload/back/forward/capture/select/draw).</summary>
    [Prop] public IWriteStream<WebViewerCommand>? Commands { get; init; }

    /// <summary>Fired for every event produced by the proxied page, the injected agent,
    /// and the service worker. The payload is a polymorphic <see cref="WebViewerEvent"/>.</summary>
    [Event] public Func<Event<WebViewer, WebViewerEvent>, ValueTask>? OnEvent { get; init; }
}

// ===========================================================================
// Commands (Ivy -> widget).
//
// IWriteStream<T>.Write serializes via the DECLARED element type (WebViewerCommand). So a
// derived record's own properties are only emitted when the base is polymorphic — hence
// [JsonPolymorphic]. The discriminator property is "action" (camelCase), which the
// frontend switches on, e.g. { "action": "select", "enabled": true }.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(ReloadCommand), "reload")]
[JsonDerivedType(typeof(BackCommand), "back")]
[JsonDerivedType(typeof(ForwardCommand), "forward")]
[JsonDerivedType(typeof(CaptureCommand), "capture")]
[JsonDerivedType(typeof(SelectModeCommand), "select")]
[JsonDerivedType(typeof(DrawModeCommand), "draw")]
[JsonDerivedType(typeof(ClearCommentsCommand), "clear-comments")]
public abstract record WebViewerCommand;

public record ReloadCommand : WebViewerCommand;

public record BackCommand : WebViewerCommand;

public record ForwardCommand : WebViewerCommand;

/// <summary>Capture a screenshot. <paramref name="Mode"/> is "page" (full) or "viewport".</summary>
public record CaptureCommand(string Mode) : WebViewerCommand;

/// <summary>Start/stop element pick-to-comment mode.</summary>
public record SelectModeCommand(bool Enabled) : WebViewerCommand;

/// <summary>Start/stop red-pen drawing mode.</summary>
public record DrawModeCommand(bool Enabled) : WebViewerCommand;

/// <summary>
/// Drop every comment and its pin. For the host that has just acted on them — sent them to an
/// agent, say — and would otherwise leave the page marked up with feedback already delivered.
/// Silent by design: no <see cref="CommentDeletedEvent"/> follows, since the host asked.
/// </summary>
public record ClearCommentsCommand : WebViewerCommand;

// ===========================================================================
// Events (widget -> Ivy).
//
// Polymorphic payload discriminated by "kind". Ivy deserializes event args against the
// declared base type (WidgetBase event path uses JsonNode.Deserialize with
// DefaultJsonTypeInfoResolver), so [JsonPolymorphic] is honored here.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ConsoleEvent), "console")]
[JsonDerivedType(typeof(ClickEvent), "click")]
[JsonDerivedType(typeof(CommentEvent), "comment")]
[JsonDerivedType(typeof(CommentUpdatedEvent), "comment-updated")]
[JsonDerivedType(typeof(CommentDeletedEvent), "comment-deleted")]
[JsonDerivedType(typeof(DrawEvent), "draw")]
[JsonDerivedType(typeof(HttpEvent), "http")]
[JsonDerivedType(typeof(NavigateEvent), "navigate")]
[JsonDerivedType(typeof(CaptureEvent), "capture")]
public abstract record WebViewerEvent;

/// <summary>A console.log/warn/error (or an uncaught error) from the proxied page.</summary>
public record ConsoleEvent(string Level, string Text, string? Stack) : WebViewerEvent;

/// <summary>A click anywhere in the proxied page. <paramref name="DebugJson"/> is the
/// source-attribution payload as a JSON string (null if none): where available it names the
/// original file/line behind the element, with a provenance and confidence saying how it was
/// derived. See the collector in proxy-assets/agent.js and the /__resolve endpoint.</summary>
public record ClickEvent(
    string Tag,
    string? Text,
    string Xpath,
    string Selector,
    int Button,
    double X,
    double Y,
    string? DebugJson) : WebViewerEvent;

/// <summary>
/// A completed comment: the user picked an element and submitted text. The widget marks the
/// element with a numbered pin and keeps it anchored there for the rest of the session,
/// through re-renders, scrolling and reloads.
///
/// <para><paramref name="Id"/> is the pin's stable identity — it is what the follow-up
/// <see cref="CommentUpdatedEvent"/> and <see cref="CommentDeletedEvent"/> name, and the only
/// field worth storing as a key. <paramref name="Number"/> is what the pin SHOWS, which is
/// just its 1-based position: delete pin 2 of 3 and the last one renumbers to 2, with no
/// event of its own. Keep the comments in arrival order and the numbers fall out of the
/// order; do not treat a number as an identity.</para>
///
/// <para><paramref name="Url"/> is the page it was left on, canonicalized by the widget: the
/// hash removed, a trailing slash removed, the query kept. Every comment on one page carries
/// the identical string, so grouping by it is plain equality and nothing else has to re-derive
/// what counts as the same page. The widget shows a pin only while its own page is on screen —
/// an xpath resolves on other pages too, and an unscoped pin does not visibly go away, it
/// re-attaches to whatever occupies the position.</para>
/// </summary>
public record CommentEvent(
    string Id,
    int Number,
    string Tag,
    string Xpath,
    string Selector,
    string Comment,
    string? DebugJson,
    string? Url = null,
    string? Text = null,
    string? AttrsJson = null,
    string? Device = null) : WebViewerEvent;

/// <summary>The text of an existing comment was edited in place (the user clicked its pin).</summary>
public record CommentUpdatedEvent(string Id, int Number, string Comment) : WebViewerEvent;

/// <summary>A comment was deleted from its pin. Remaining pins renumber; see
/// <see cref="CommentEvent"/>.</summary>
public record CommentDeletedEvent(string Id, int Number) : WebViewerEvent;

/// <summary>A red-pen stroke. <paramref name="PointsJson"/> is the raw points array as JSON.</summary>
public record DrawEvent(int PointCount, string PointsJson) : WebViewerEvent;

/// <summary>A network request (HAR-style) intercepted by the service worker.</summary>
public record HttpEvent(
    string Url,
    string Method,
    int Status,
    string ResourceType,
    long Size,
    double Time) : WebViewerEvent;

/// <summary>The viewed page's URL changed (initial load, link click, or back/forward).</summary>
public record NavigateEvent(string Url, bool CanGoBack, bool CanGoForward) : WebViewerEvent;

/// <summary>A screenshot was saved server-side. <paramref name="Url"/>/<paramref name="Path"/>
/// come from the /__capture endpoint.</summary>
public record CaptureEvent(string Url, string Path, int Width, int Height, string Mode) : WebViewerEvent;

// ===========================================================================

public static class WebViewerExtensions
{
    public static WebViewer Url(this WebViewer w, string? url) =>
        w with { Url = url };

    public static WebViewer Device(this WebViewer w, WebViewerDevice device) =>
        w with { Device = device };

    public static WebViewer Commands(this WebViewer w, IWriteStream<WebViewerCommand> commands) =>
        w with { Commands = commands };

    // NOTE: named WithOnEvent, not OnEvent. A fluent method whose name matches a
    // delegate-typed property (OnEvent) is shadowed by delegate-invocation member access
    // for single-parameter lambdas — `.OnEvent(e => ...)` would bind to the property and
    // fail with CS1660. The property must stay OnEvent (the serializer emits the event
    // name from it). Same convention as SortableVerificationList.WithOnReorder.
    public static WebViewer WithOnEvent(
        this WebViewer w,
        Func<Event<WebViewer, WebViewerEvent>, ValueTask> handler
    ) => w with { OnEvent = handler };

    public static WebViewer WithOnEvent(this WebViewer w, Action<WebViewerEvent> handler) =>
        w with
        {
            OnEvent = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };
}

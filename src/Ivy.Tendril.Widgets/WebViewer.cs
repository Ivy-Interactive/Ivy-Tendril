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
/// meant to be built in Ivy code; the widget owns only the iframe and the comment overlay.
///
/// The proxy/capture/service-worker endpoints (/__proxy, /__view, /__capture, /__lib,
/// /sw.js) must be hosted by the Ivy app on the same origin (see .samples/WebViewerProxy.cs).
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
[JsonDerivedType(typeof(DrawEvent), "draw")]
[JsonDerivedType(typeof(HttpEvent), "http")]
[JsonDerivedType(typeof(NavigateEvent), "navigate")]
[JsonDerivedType(typeof(CaptureEvent), "capture")]
public abstract record WebViewerEvent;

/// <summary>A console.log/warn/error (or an uncaught error) from the proxied page.</summary>
public record ConsoleEvent(string Level, string Text, string? Stack) : WebViewerEvent;

/// <summary>A click anywhere in the proxied page. <paramref name="ReactJson"/> is the
/// detected component/source info as a JSON string (null if none).</summary>
public record ClickEvent(
    string Tag,
    string? Text,
    string Xpath,
    string Selector,
    int Button,
    double X,
    double Y,
    string? ReactJson) : WebViewerEvent;

/// <summary>A completed comment: the user picked an element and submitted text.</summary>
public record CommentEvent(
    string Tag,
    string Xpath,
    string Selector,
    string Comment,
    string? ReactJson) : WebViewerEvent;

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

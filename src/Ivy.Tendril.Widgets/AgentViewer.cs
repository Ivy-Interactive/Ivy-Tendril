namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "AgentViewer",
    GlobalName = "IvyTendrilWidgets"
)]
public record AgentViewer : WidgetBase<AgentViewer>
{
    /// <summary>Pre-buffered newline-delimited EventWire JSON events.</summary>
    [Prop] public string? JsonStream { get; init; }

    /// <summary>Live streaming input (EventWire JSON lines).</summary>
    [Prop] public IWriteStream<string>? Stream { get; init; }

    /// <summary>Auto-scroll to bottom as new events arrive.</summary>
    [Prop] public bool AutoScroll { get; init; } = true;

    /// <summary>Show thinking/reasoning blocks.</summary>
    [Prop] public bool ShowThinking { get; init; } = false;

    /// <summary>Show system init events.</summary>
    [Prop] public bool ShowSystemEvents { get; init; } = false;

    /// <summary>Show the animated status label at the bottom of the view.</summary>
    [Prop] public bool ShowStatusLabel { get; init; } = true;

    /// <summary>Override the auto-derived status label text. Null = derive from latest event.</summary>
    [Prop] public string? StatusLabelOverride { get; init; }

    [Event] public Func<Event<AgentViewer, string>, ValueTask>? OnComplete { get; init; }
}

public static class AgentViewerExtensions
{
    public static AgentViewer JsonStream(this AgentViewer w, string? jsonStream) =>
        w with { JsonStream = jsonStream };

    public static AgentViewer Stream(this AgentViewer w, IWriteStream<string> stream) =>
        w with { Stream = stream };

    public static AgentViewer AutoScroll(this AgentViewer w, bool autoScroll = true) =>
        w with { AutoScroll = autoScroll };

    public static AgentViewer ShowThinking(this AgentViewer w, bool showThinking = true) =>
        w with { ShowThinking = showThinking };

    public static AgentViewer ShowSystemEvents(this AgentViewer w, bool showSystemEvents = true) =>
        w with { ShowSystemEvents = showSystemEvents };

    public static AgentViewer ShowStatusLabel(this AgentViewer w, bool showStatusLabel = true) =>
        w with { ShowStatusLabel = showStatusLabel };

    public static AgentViewer StatusLabel(this AgentViewer w, string? statusLabel) =>
        w with { StatusLabelOverride = statusLabel };

    public static AgentViewer OnComplete(
        this AgentViewer w,
        Func<Event<AgentViewer, string>, ValueTask> handler
    ) => w with { OnComplete = handler };

    public static AgentViewer OnComplete(this AgentViewer w, Action<string> handler) =>
        w with
        {
            OnComplete = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };
}

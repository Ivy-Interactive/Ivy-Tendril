using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;

namespace Ivy.Widgets.AgentOutput;

[ExternalWidget(
    "Widgets/frontend/dist/ivy-tendril-widgets.js",
    StylePath = "Widgets/frontend/dist/ivy-tendril-widgets.css",
    ExportName = "AgentOutput",
    GlobalName = "IvyTendrilWidgets"
)]
public record AgentOutput : WidgetBase<AgentOutput>
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

    [Event] public Func<Event<AgentOutput, string>, ValueTask>? OnComplete { get; init; }
}

public static class AgentOutputExtensions
{
    public static AgentOutput JsonStream(this AgentOutput w, string? jsonStream) =>
        w with { JsonStream = jsonStream };

    public static AgentOutput Stream(this AgentOutput w, IWriteStream<string> stream) =>
        w with { Stream = stream };

    public static AgentOutput AutoScroll(this AgentOutput w, bool autoScroll = true) =>
        w with { AutoScroll = autoScroll };

    public static AgentOutput ShowThinking(this AgentOutput w, bool showThinking = true) =>
        w with { ShowThinking = showThinking };

    public static AgentOutput ShowSystemEvents(this AgentOutput w, bool showSystemEvents = true) =>
        w with { ShowSystemEvents = showSystemEvents };

    public static AgentOutput ShowStatusLabel(this AgentOutput w, bool showStatusLabel = true) =>
        w with { ShowStatusLabel = showStatusLabel };

    public static AgentOutput StatusLabel(this AgentOutput w, string? statusLabel) =>
        w with { StatusLabelOverride = statusLabel };

    public static AgentOutput OnComplete(
        this AgentOutput w,
        Func<Event<AgentOutput, string>, ValueTask> handler
    ) => w with { OnComplete = handler };

    public static AgentOutput OnComplete(this AgentOutput w, Action<string> handler) =>
        w with
        {
            OnComplete = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };
}

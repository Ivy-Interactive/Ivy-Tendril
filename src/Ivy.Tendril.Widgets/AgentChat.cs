using System;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;

namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "AgentChat",
    GlobalName = "IvyTendrilWidgets"
)]
public record AgentChat : WidgetBase<AgentChat>
{
    public AgentChat()
    {
    }

    [Prop] public AgentChatMessage[] Messages { get; init; } = Array.Empty<AgentChatMessage>();
    [Prop] public bool IsStreaming { get; init; }
    [Prop] public IWriteStream<string>? Stream { get; init; }
    [Prop] public string Placeholder { get; init; } = "Ask the agent anything...";

    [Event] public EventHandler<Event<AgentChat, string>>? OnSend { get; set; }
    [Event] public EventHandler<Event<AgentChat>>? OnCancel { get; set; }
}

public record AgentChatMessage(string Sender, string Content);

public static class AgentChatExtensions
{
    public static AgentChat Messages(this AgentChat w, AgentChatMessage[] messages) =>
        w with { Messages = messages };

    public static AgentChat IsStreaming(this AgentChat w, bool isStreaming) =>
        w with { IsStreaming = isStreaming };

    public static AgentChat Stream(this AgentChat w, IWriteStream<string>? stream) =>
        w with { Stream = stream };

    public static AgentChat Placeholder(this AgentChat w, string placeholder) =>
        w with { Placeholder = placeholder };

    public static AgentChat OnSend(this AgentChat w, Action<Event<AgentChat, string>> handler) =>
        w with { OnSend = handler.ToEventHandler() };

    public static AgentChat OnCancel(this AgentChat w, Action<Event<AgentChat>> handler) =>
        w with { OnCancel = handler.ToEventHandler() };
}

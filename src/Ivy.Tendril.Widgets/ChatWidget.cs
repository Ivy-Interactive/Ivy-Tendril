using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ivy.Core;

namespace Ivy.Tendril.Widgets;

public record ChatMessageDto(
    string Id,
    string Role,
    string Content,
    string Timestamp,
    string? AgentId = null,
    string? ModelId = null
);

public record ChatSessionDto(
    string Id,
    string Title,
    string AgentId,
    string ModelId,
    string CreatedAt,
    string UpdatedAt,
    List<ChatMessageDto> Messages
);

public record AgentOptionDto(string Id, string Label);
public record ModelOptionDto(string Id, string DisplayName);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ChatWidget",
    GlobalName = "IvyTendrilWidgets"
)]
public record ChatWidget : WidgetBase<ChatWidget>
{
    [Prop] public string? ActiveSessionId { get; init; }
    [Prop] public List<ChatSessionDto> Sessions { get; init; } = new();
    [Prop] public List<AgentOptionDto> Agents { get; init; } = new();
    [Prop] public List<ModelOptionDto> Models { get; init; } = new();
    [Prop] public string SelectedAgent { get; init; } = "claude";
    [Prop] public string SelectedModel { get; init; } = "opus";
    [Prop] public bool IsStreaming { get; init; } = false;
    [Prop] public string? StreamingText { get; init; }

    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnSelectSession { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnDeleteSession { get; init; }
    [Event] public Func<Event<ChatWidget, object>, ValueTask>? OnCreateSession { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnSendMessage { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnAgentChanged { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnModelChanged { get; init; }
}

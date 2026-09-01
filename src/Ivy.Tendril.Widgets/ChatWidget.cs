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
    string? ModelId = null,
    string? RawStream = null,
    string? Effort = null
);

public record ChatSessionDto(
    string Id,
    string Title,
    string AgentId,
    string ModelId,
    string CreatedAt,
    string UpdatedAt,
    List<ChatMessageDto> Messages,
    string Status = "done",
    string? Effort = null
);

public record AgentOptionDto(string Id, string Label);
public record ModelOptionDto(string Id, string DisplayName);
public record EffortOptionDto(string Id, string DisplayName);

public record ChatAttachmentDto(
    string Name,
    string ContentType,
    long Size,
    string? Base64Data = null,
    string? LocalPath = null,
    string? FileId = null
);

public record ChatQueuedMessageDto(
    string Id,
    string Prompt,
    List<ChatAttachmentDto>? Attachments = null
);

public record ChatSendMessageDto(
    string Prompt,
    List<ChatAttachmentDto>? Attachments = null,
    string? SessionId = null
);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ChatWidget",
    GlobalName = "IvyTendrilWidgets"
)]
public record ChatWidget : WidgetBase<ChatWidget>
{
    [Prop] public string? ActiveSessionId { get; init; }
    [Prop] public string? StreamingSessionId { get; init; }
    [Prop] public string? UploadUrl { get; init; }
    [Prop] public List<ChatSessionDto> Sessions { get; init; } = new();
    [Prop] public List<AgentOptionDto> Agents { get; init; } = new();
    [Prop] public List<ModelOptionDto> Models { get; init; } = new();
    [Prop] public List<EffortOptionDto> Efforts { get; init; } = new();
    [Prop] public string SelectedAgent { get; init; } = "claude";
    [Prop] public string SelectedModel { get; init; } = "opus";
    [Prop] public string SelectedEffort { get; init; } = "default";
    [Prop] public bool SupportsEffort { get; init; } = true;
    [Prop] public bool IsStreaming { get; init; } = false;
    [Prop] public string? StreamingText { get; init; }
    [Prop] public IWriteStream<string>? StreamingStream { get; init; }
    [Prop] public List<ChatQueuedMessageDto> QueuedMessages { get; init; } = new();

    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnSelectSession { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnDeleteSession { get; init; }
    [Event] public Func<Event<ChatWidget, string[]>, ValueTask>? OnRenameSession { get; init; }
    [Event] public Func<Event<ChatWidget, object>, ValueTask>? OnCreateSession { get; init; }
    [Event] public Func<Event<ChatWidget, ChatSendMessageDto>, ValueTask>? OnSendMessage { get; init; }
    [Event] public Func<Event<ChatWidget, object>, ValueTask>? OnCancelStream { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnAgentChanged { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnModelChanged { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnEffortChanged { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnDeleteQueuedMessage { get; init; }
    [Event] public Func<Event<ChatWidget, string[]>, ValueTask>? OnUpdateQueuedMessage { get; init; }
    [Event] public Func<Event<ChatWidget, string>, ValueTask>? OnSendQueuedNow { get; init; }
}

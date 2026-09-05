using System;
using System.Collections.Generic;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Services;

public record ChatQueuedItem(
    string Id,
    string Prompt,
    List<ChatAttachmentDto>? Attachments,
    DateTimeOffset CreatedAt
);

public record ChatMessageModel(
    string Id,
    string Role,
    string Content,
    DateTimeOffset Timestamp,
    string? AgentId = null,
    string? ModelId = null,
    string? RawStream = null,
    string? Effort = null
);

public record ChatSessionModel(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string AgentId,
    string ModelId,
    List<ChatMessageModel> Messages,
    string? Effort = null,
    List<string>? SpawnedJobIds = null
);

public interface IChatHistoryService
{
    event EventHandler? SessionsChanged;
    event EventHandler? GeneratingSessionsChanged;
    IReadOnlyList<ChatSessionModel> GetSessions();
    ChatSessionModel? GetSession(string id);
    ChatSessionModel CreateSession(string agentId, string modelId, string? title = null, string? effort = null);
    void SaveSession(ChatSessionModel session);
    void DeleteSession(string id);
    void RenameSession(string id, string newTitle);
    ChatMessageModel AddMessage(string sessionId, string role, string content, string? agentId = null, string? modelId = null, string? rawStream = null, string? effort = null);
    void SetSessionGenerating(string sessionId, bool isGenerating);
    void ClearAllGeneratingSessions();
    IReadOnlySet<string> GetGeneratingSessionIds();
    IReadOnlySet<string> GetCompletedSessionIds();
    void ClearSessionCompleted(string sessionId);
    IReadOnlyList<ChatQueuedItem> GetQueuedMessages(string sessionId);
    ChatQueuedItem EnqueueMessage(string sessionId, ChatSendMessageDto dto);
    bool TryDequeueMessage(string sessionId, out ChatQueuedItem? item);
    bool RemoveQueuedMessage(string sessionId, string queueId);
    bool UpdateQueuedMessage(string sessionId, string queueId, string prompt);
    void ClearQueuedMessages(string sessionId);
    void AddSpawnedJob(string sessionId, string jobId);
    IReadOnlyList<string> GetSpawnedJobs(string sessionId);
}

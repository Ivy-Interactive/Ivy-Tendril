using System;
using System.Collections.Generic;

namespace Ivy.Tendril.Services;

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
    string? Effort = null
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
    IReadOnlySet<string> GetGeneratingSessionIds();
    IReadOnlySet<string> GetCompletedSessionIds();
    void ClearSessionCompleted(string sessionId);
}

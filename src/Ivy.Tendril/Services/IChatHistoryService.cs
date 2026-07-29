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
    string? RawStream = null
);

public record ChatSessionModel(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string AgentId,
    string ModelId,
    List<ChatMessageModel> Messages
);

public interface IChatHistoryService
{
    IReadOnlyList<ChatSessionModel> GetSessions();
    ChatSessionModel? GetSession(string id);
    ChatSessionModel CreateSession(string agentId, string modelId, string? title = null);
    void SaveSession(ChatSessionModel session);
    void DeleteSession(string id);
    void RenameSession(string id, string newTitle);
    ChatMessageModel AddMessage(string sessionId, string role, string content, string? agentId = null, string? modelId = null, string? rawStream = null);
}

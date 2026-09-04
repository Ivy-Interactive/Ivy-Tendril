using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Services;

public class ChatHistoryService : IChatHistoryService
{
    private readonly IConfigService _configService;
    private readonly ConcurrentDictionary<string, ChatSessionModel> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _generatingSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _completedSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ChatQueuedItem>> _queuedMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public event EventHandler? SessionsChanged;
    public event EventHandler? GeneratingSessionsChanged;

    public void SetSessionGenerating(string sessionId, bool isGenerating)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        bool changed;
        if (isGenerating)
        {
            _completedSessions.TryRemove(sessionId, out _);
            changed = _generatingSessions.TryAdd(sessionId, 0);
        }
        else
        {
            changed = _generatingSessions.TryRemove(sessionId, out _);
            _completedSessions.TryAdd(sessionId, 0);
        }

        if (changed)
        {
            GeneratingSessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearAllGeneratingSessions()
    {
        if (!_generatingSessions.IsEmpty)
        {
            _generatingSessions.Clear();
            GeneratingSessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlySet<string> GetGeneratingSessionIds()
    {
        return _generatingSessions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> GetCompletedSessionIds()
    {
        return _completedSessions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void ClearSessionCompleted(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        if (_completedSessions.TryRemove(sessionId, out _))
        {
            GeneratingSessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<ChatQueuedItem> GetQueuedMessages(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return Array.Empty<ChatQueuedItem>();
        lock (_lock)
        {
            if (_queuedMessages.TryGetValue(sessionId, out var list))
            {
                return list.ToList();
            }
            return Array.Empty<ChatQueuedItem>();
        }
    }

    public ChatQueuedItem EnqueueMessage(string sessionId, ChatSendMessageDto dto)
    {
        var item = new ChatQueuedItem(
            Id: Guid.NewGuid().ToString("N"),
            Prompt: dto.Prompt,
            Attachments: dto.Attachments != null ? new List<ChatAttachmentDto>(dto.Attachments) : null,
            CreatedAt: DateTimeOffset.UtcNow
        );
        lock (_lock)
        {
            if (!_queuedMessages.TryGetValue(sessionId, out var list))
            {
                list = new List<ChatQueuedItem>();
                _queuedMessages[sessionId] = list;
            }
            list.Add(item);
        }
        GeneratingSessionsChanged?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public bool TryDequeueMessage(string sessionId, out ChatQueuedItem? item)
    {
        item = null;
        if (string.IsNullOrEmpty(sessionId)) return false;
        bool dequeued = false;
        lock (_lock)
        {
            if (_queuedMessages.TryGetValue(sessionId, out var list) && list.Count > 0)
            {
                item = list[0];
                list.RemoveAt(0);
                if (list.Count == 0)
                {
                    _queuedMessages.TryRemove(sessionId, out _);
                }
                dequeued = true;
            }
        }
        if (dequeued)
        {
            GeneratingSessionsChanged?.Invoke(this, EventArgs.Empty);
        }
        return dequeued;
    }

    public bool RemoveQueuedMessage(string sessionId, string queueId)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(queueId)) return false;
        bool removed = false;
        lock (_lock)
        {
            if (_queuedMessages.TryGetValue(sessionId, out var list))
            {
                var count = list.RemoveAll(q => q.Id == queueId);
                if (count > 0)
                {
                    removed = true;
                    if (list.Count == 0)
                    {
                        _queuedMessages.TryRemove(sessionId, out _);
                    }
                }
            }
        }
        if (removed)
        {
            GeneratingSessionsChanged?.Invoke(this, EventArgs.Empty);
        }
        return removed;
    }

    public bool UpdateQueuedMessage(string sessionId, string queueId, string prompt)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(queueId)) return false;
        bool updated = false;
        lock (_lock)
        {
            if (_queuedMessages.TryGetValue(sessionId, out var list))
            {
                var idx = list.FindIndex(q => q.Id == queueId);
                if (idx >= 0)
                {
                    list[idx] = list[idx] with { Prompt = prompt };
                    updated = true;
                }
            }
        }
        if (updated)
        {
            GeneratingSessionsChanged?.Invoke(this, EventArgs.Empty);
        }
        return updated;
    }

    public void ClearQueuedMessages(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        bool cleared = false;
        lock (_lock)
        {
            cleared = _queuedMessages.TryRemove(sessionId, out _);
        }
        if (cleared)
        {
            GeneratingSessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public ChatHistoryService(IConfigService configService)
    {
        _configService = configService;
        LoadSessionsFromDisk();
    }

    private string GetStorageDir()
    {
        var dir = Path.Combine(_configService.TendrilHome, "Chats");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    private static string CleanTitle(string? rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle)) return "New Chat";
        var title = rawTitle.Trim();
        while (title.EndsWith("...") || title.EndsWith("…"))
        {
            if (title.EndsWith("..."))
                title = title[..^3].TrimEnd();
            else if (title.EndsWith("…"))
                title = title[..^1].TrimEnd();
        }
        return string.IsNullOrWhiteSpace(title) ? "New Chat" : title;
    }

    private void LoadSessionsFromDisk()
    {
        try
        {
            var dir = GetStorageDir();
            var files = Directory.GetFiles(dir, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = Ivy.Tendril.Helpers.FileHelper.ReadAllText(file);
                    var session = JsonSerializer.Deserialize<ChatSessionModel>(json, JsonOptions);
                    if (session != null && !string.IsNullOrEmpty(session.Id))
                    {
                        var cleanedTitle = CleanTitle(session.Title);
                        if (cleanedTitle != session.Title)
                        {
                            session = session with { Title = cleanedTitle };
                        }
                        _sessions[session.Id] = session;
                    }
                }
                catch
                {
                    // Ignore corrupted single session files gracefully
                }
            }
        }
        catch
        {
            // Ignore directory creation / read issues on startup
        }
    }

    public IReadOnlyList<ChatSessionModel> GetSessions()
    {
        return _sessions.Values
            .OrderByDescending(s => s.UpdatedAt)
            .ToList();
    }

    public ChatSessionModel? GetSession(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        _sessions.TryGetValue(id, out var session);
        return session;
    }

    public ChatSessionModel CreateSession(string agentId, string modelId, string? title = null, string? effort = null)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        var displayTitle = !string.IsNullOrWhiteSpace(title) ? CleanTitle(title) : "New Chat";

        var session = new ChatSessionModel(
            Id: id,
            Title: displayTitle,
            CreatedAt: now,
            UpdatedAt: now,
            AgentId: agentId,
            ModelId: modelId,
            Messages: new List<ChatMessageModel>(),
            Effort: effort
        );

        _sessions[id] = session;
        PersistSessionToDisk(session);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return session;
    }

    public void SaveSession(ChatSessionModel session)
    {
        if (session == null || string.IsNullOrEmpty(session.Id)) return;
        _sessions[session.Id] = session;
        PersistSessionToDisk(session);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSession(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _sessions.TryRemove(id, out _);
        _queuedMessages.TryRemove(id, out _);

        try
        {
            var filePath = Path.Combine(GetStorageDir(), $"{id}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best effort file deletion
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RenameSession(string id, string newTitle)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(newTitle)) return;
        lock (_lock)
        {
            var session = GetSession(id);
            if (session == null) return;
            var updated = session with
            {
                Title = CleanTitle(newTitle),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _sessions[id] = updated;
            PersistSessionToDisk(updated);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ChatMessageModel AddMessage(string sessionId, string role, string content, string? agentId = null, string? modelId = null, string? rawStream = null, string? effort = null)
    {
        lock (_lock)
        {
            var session = GetSession(sessionId);
            if (session == null)
            {
                session = CreateSession(agentId ?? "claude", modelId ?? "opus", effort: effort);
            }

            var msg = new ChatMessageModel(
                Id: Guid.NewGuid().ToString("N"),
                Role: role,
                Content: content,
                Timestamp: DateTimeOffset.UtcNow,
                AgentId: agentId ?? session.AgentId,
                ModelId: modelId ?? session.ModelId,
                RawStream: rawStream,
                Effort: effort ?? session.Effort
            );

            var updatedMessages = new List<ChatMessageModel>(session.Messages) { msg };

            var updatedSession = session with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                AgentId = agentId ?? session.AgentId,
                ModelId = modelId ?? session.ModelId,
                Effort = effort ?? session.Effort,
                Messages = updatedMessages
            };

            _sessions[session.Id] = updatedSession;
            PersistSessionToDisk(updatedSession);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
            return msg;
        }
    }

    private void PersistSessionToDisk(ChatSessionModel session)
    {
        try
        {
            var filePath = Path.Combine(GetStorageDir(), $"{session.Id}.json");
            var json = JsonSerializer.Serialize(session, JsonOptions);
            Ivy.Tendril.Helpers.FileHelper.WriteAllText(filePath, json);
        }
        catch
        {
            // Best effort write
        }
    }
}

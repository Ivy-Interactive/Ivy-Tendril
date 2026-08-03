using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Ivy.Tendril.Services;

public class ChatHistoryService : IChatHistoryService
{
    private readonly IConfigService _configService;
    private readonly ConcurrentDictionary<string, ChatSessionModel> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _generatingSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public event EventHandler? SessionsChanged;
    public event EventHandler? GeneratingSessionsChanged;

    public void SetSessionGenerating(string sessionId, bool isGenerating)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        bool changed;
        if (isGenerating)
        {
            changed = _generatingSessions.TryAdd(sessionId, 0);
        }
        else
        {
            changed = _generatingSessions.TryRemove(sessionId, out _);
        }

        if (changed)
        {
            GeneratingSessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlySet<string> GetGeneratingSessionIds()
    {
        return _generatingSessions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                    var json = File.ReadAllText(file);
                    var session = JsonSerializer.Deserialize<ChatSessionModel>(json, JsonOptions);
                    if (session != null && !string.IsNullOrEmpty(session.Id))
                    {
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

    public ChatSessionModel CreateSession(string agentId, string modelId, string? title = null)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        var displayTitle = !string.IsNullOrWhiteSpace(title) ? title.Trim() : "New Chat";

        var session = new ChatSessionModel(
            Id: id,
            Title: displayTitle,
            CreatedAt: now,
            UpdatedAt: now,
            AgentId: agentId,
            ModelId: modelId,
            Messages: new List<ChatMessageModel>()
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
                Title = newTitle.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _sessions[id] = updated;
            PersistSessionToDisk(updated);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ChatMessageModel AddMessage(string sessionId, string role, string content, string? agentId = null, string? modelId = null, string? rawStream = null)
    {
        lock (_lock)
        {
            var session = GetSession(sessionId);
            if (session == null)
            {
                session = CreateSession(agentId ?? "claude", modelId ?? "opus");
            }

            var msg = new ChatMessageModel(
                Id: Guid.NewGuid().ToString("N"),
                Role: role,
                Content: content,
                Timestamp: DateTimeOffset.UtcNow,
                AgentId: agentId ?? session.AgentId,
                ModelId: modelId ?? session.ModelId,
                RawStream: rawStream
            );

            var updatedMessages = new List<ChatMessageModel>(session.Messages) { msg };

            // Auto update title from first user message if title is "New Chat"
            var title = session.Title;
            if ((title == "New Chat" || string.IsNullOrWhiteSpace(title)) && role == "user" && !string.IsNullOrWhiteSpace(content))
            {
                title = content.Length > 30 ? content[..30] + "..." : content;
            }

            var updatedSession = session with
            {
                Title = title,
                UpdatedAt = DateTimeOffset.UtcNow,
                AgentId = agentId ?? session.AgentId,
                ModelId = modelId ?? session.ModelId,
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
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Best effort write
        }
    }
}

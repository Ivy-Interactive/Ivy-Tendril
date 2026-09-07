using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class ChatHistoryService : IChatHistoryService
{
    private readonly IConfigService _configService;
    private readonly ILogger<ChatHistoryService>? _logger;
    private readonly ConcurrentDictionary<string, ChatSessionModel> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _generatingSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _completedSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ChatQueuedItem>> _queuedMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastPersistTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sessionLock = new();
    private readonly object _queueLock = new();

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
        lock (_queueLock)
        {
            if (_queuedMessages.TryGetValue(sessionId, out var list))
            {
                list.RemoveAll(q => q.Prompt != null && q.Prompt.StartsWith("[System Event]", StringComparison.OrdinalIgnoreCase));
                return list.ToList();
            }
            return Array.Empty<ChatQueuedItem>();
        }
    }

    public ChatQueuedItem EnqueueMessage(string sessionId, ChatSendMessageDto dto)
    {
        if (dto.Prompt != null && dto.Prompt.StartsWith("[System Event]", StringComparison.OrdinalIgnoreCase))
        {
            return new ChatQueuedItem(Guid.NewGuid().ToString("N"), dto.Prompt, dto.Attachments != null ? new List<ChatAttachmentDto>(dto.Attachments) : null, DateTimeOffset.UtcNow);
        }

        var item = new ChatQueuedItem(
            Id: Guid.NewGuid().ToString("N"),
            Prompt: dto.Prompt,
            Attachments: dto.Attachments != null ? new List<ChatAttachmentDto>(dto.Attachments) : null,
            CreatedAt: DateTimeOffset.UtcNow
        );
        lock (_queueLock)
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
        lock (_queueLock)
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
        lock (_queueLock)
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
        lock (_queueLock)
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
        lock (_queueLock)
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

    public ChatHistoryService(IConfigService configService, ILogger<ChatHistoryService>? logger = null)
    {
        _configService = configService;
        _logger = logger;
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
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to load chat session from file {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load chat sessions from disk");
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
        CancelPendingPersist(session.Id);
        _lastPersistTimes[session.Id] = DateTimeOffset.UtcNow;
        PersistSessionToDisk(session);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSession(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        CancelPendingPersist(id);
        _lastPersistTimes.TryRemove(id, out _);
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
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete chat session file for {SessionId}", id);
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RenameSession(string id, string newTitle)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(newTitle)) return;
        ChatSessionModel? updated = null;
        lock (_sessionLock)
        {
            var session = GetSession(id);
            if (session == null) return;
            updated = session with
            {
                Title = CleanTitle(newTitle),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _sessions[id] = updated;
        }

        if (updated != null)
        {
            CancelPendingPersist(id);
            _lastPersistTimes[id] = DateTimeOffset.UtcNow;
            PersistSessionToDisk(updated);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ChatMessageModel AddMessage(string sessionId, string role, string content, string? agentId = null, string? modelId = null, string? rawStream = null, string? effort = null)
    {
        ChatMessageModel msg;
        ChatSessionModel updatedSession;
        lock (_sessionLock)
        {
            var session = GetSession(sessionId);
            if (session == null)
            {
                session = CreateSession(agentId ?? "claude", modelId ?? "opus", effort: effort);
            }

            msg = new ChatMessageModel(
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

            updatedSession = session with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                AgentId = agentId ?? session.AgentId,
                ModelId = modelId ?? session.ModelId,
                Effort = effort ?? session.Effort,
                Messages = updatedMessages
            };

            _sessions[session.Id] = updatedSession;
        }

        CancelPendingPersist(updatedSession.Id);
        _lastPersistTimes[updatedSession.Id] = DateTimeOffset.UtcNow;
        PersistSessionToDisk(updatedSession);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return msg;
    }

    public void AddSpawnedJob(string sessionId, string jobId)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(jobId)) return;
        ChatSessionModel? updated = null;
        lock (_sessionLock)
        {
            var session = GetSession(sessionId);
            if (session == null) return;
            var currentJobs = session.SpawnedJobIds != null ? new List<string>(session.SpawnedJobIds) : new List<string>();
            if (!currentJobs.Contains(jobId, StringComparer.OrdinalIgnoreCase))
            {
                currentJobs.Add(jobId);
                updated = session with
                {
                    SpawnedJobIds = currentJobs
                };
                _sessions[sessionId] = updated;
            }
        }

        if (updated != null)
        {
            PersistSessionToDisk(updated);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemoveSpawnedJobs(string sessionId, IEnumerable<string> jobIds)
    {
        if (string.IsNullOrEmpty(sessionId) || jobIds == null) return;
        ChatSessionModel? updated = null;
        lock (_sessionLock)
        {
            var session = GetSession(sessionId);
            if (session == null || session.SpawnedJobIds == null || session.SpawnedJobIds.Count == 0) return;
            var toRemove = new HashSet<string>(jobIds, StringComparer.OrdinalIgnoreCase);
            var remaining = session.SpawnedJobIds.Where(id => !toRemove.Contains(id)).ToList();
            if (remaining.Count != session.SpawnedJobIds.Count)
            {
                updated = session with
                {
                    SpawnedJobIds = remaining.Count > 0 ? remaining : null
                };
                _sessions[sessionId] = updated;
            }
        }

        if (updated != null)
        {
            PersistSessionToDisk(updated);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<string> GetSpawnedJobs(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return Array.Empty<string>();
        var session = GetSession(sessionId);
        return session?.SpawnedJobIds ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public bool ApplyQuestionAnswers(string sessionId, string messageId, IReadOnlyDictionary<string, string[]> answers)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(messageId) || answers == null || answers.Count == 0)
            return false;

        ChatSessionModel? updatedSession = null;
        lock (_sessionLock)
        {
            var session = GetSession(sessionId);
            if (session == null) return false;

            var msgIndex = session.Messages.FindIndex(m => string.Equals(m.Id, messageId, StringComparison.OrdinalIgnoreCase));
            if (msgIndex < 0) return false;

            var targetMsg = session.Messages[msgIndex];
            var content = targetMsg.Content;
            bool modified = false;

            foreach (var (qId, ansValues) in answers)
            {
                var qa = new QuestionAnswer(qId, ansValues);
                if (QuestionAnswers.TryApply(content, qa, out var updatedContent))
                {
                    content = updatedContent;
                    modified = true;
                }
            }

            if (!modified) return false;

            string? rawStream = targetMsg.RawStream;
            if (!string.IsNullOrEmpty(rawStream))
            {
                var lines = rawStream.Split('\n');
                bool rawModified = false;
                bool anyTextApplied = false;
                bool hasDeltaText = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    try
                    {
                        var node = System.Text.Json.Nodes.JsonNode.Parse(line);
                        if (node is System.Text.Json.Nodes.JsonObject obj)
                        {
                            bool lineChanged = false;
                            if (obj.TryGetPropertyValue("delta", out var deltaNode) && deltaNode != null && deltaNode.GetValue<bool>())
                            {
                                hasDeltaText = true;
                            }
                            if (obj.TryGetPropertyValue("text", out var textNode) && textNode != null)
                            {
                                var textVal = textNode.GetValue<string>();
                                foreach (var (qId, ansValues) in answers)
                                {
                                    var qa = new QuestionAnswer(qId, ansValues);
                                    if (QuestionAnswers.TryApply(textVal, qa, out var updatedTextVal))
                                    {
                                        textVal = updatedTextVal;
                                        lineChanged = true;
                                        anyTextApplied = true;
                                    }
                                }
                                if (lineChanged) obj["text"] = textVal;
                            }
                            if (obj.TryGetPropertyValue("response", out var respNode) && respNode != null)
                            {
                                var respVal = respNode.GetValue<string>();
                                foreach (var (qId, ansValues) in answers)
                                {
                                    var qa = new QuestionAnswer(qId, ansValues);
                                    if (QuestionAnswers.TryApply(respVal, qa, out var updatedRespVal))
                                    {
                                        respVal = updatedRespVal;
                                        lineChanged = true;
                                    }
                                }
                                if (lineChanged) obj["response"] = respVal;
                            }
                            if (lineChanged)
                            {
                                lines[i] = obj.ToJsonString();
                                rawModified = true;
                            }
                        }
                    }
                    catch
                    {
                        // ignore malformed lines
                    }
                }

                if (hasDeltaText && !anyTextApplied && modified)
                {
                    var newLines = new List<string>();
                    bool consolidatedInserted = false;
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        bool isDeltaText = false;
                        try
                        {
                            var node = System.Text.Json.Nodes.JsonNode.Parse(trimmed);
                            if (node is System.Text.Json.Nodes.JsonObject obj &&
                                obj.TryGetPropertyValue("kind", out var kind) && kind?.GetValue<string>() == "text" &&
                                obj.TryGetPropertyValue("delta", out var delta) && delta != null && delta.GetValue<bool>())
                            {
                                isDeltaText = true;
                            }
                        }
                        catch { }

                        if (isDeltaText)
                        {
                            if (!consolidatedInserted)
                            {
                                var consolidated = new System.Text.Json.Nodes.JsonObject
                                {
                                    ["kind"] = "text",
                                    ["text"] = content,
                                    ["delta"] = false
                                };
                                newLines.Add(consolidated.ToJsonString());
                                consolidatedInserted = true;
                                rawModified = true;
                            }
                        }
                        else
                        {
                            newLines.Add(trimmed);
                        }
                    }
                    lines = newLines.ToArray();
                }

                if (rawModified)
                {
                    rawStream = string.Join("\n", lines);
                }
            }

            var updatedMsg = targetMsg with { Content = content, RawStream = rawStream };
            var updatedMessages = new List<ChatMessageModel>(session.Messages);
            updatedMessages[msgIndex] = updatedMsg;

            updatedSession = session with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Messages = updatedMessages
            };
            _sessions[session.Id] = updatedSession;
        }

        if (updatedSession != null)
        {
            PersistSessionToDisk(updatedSession);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    public ChatMessageModel? UpdateMessage(string sessionId, string messageId, string content, string? rawStream = null, bool flushImmediately = true, bool touchUpdatedAt = true)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(messageId)) return null;

        ChatMessageModel? updatedMsg = null;
        ChatSessionModel? updatedSession = null;

        lock (_sessionLock)
        {
            var session = GetSession(sessionId);
            if (session == null) return null;

            var msgIndex = session.Messages.FindIndex(m => m.Id == messageId);
            if (msgIndex < 0) return null;

            var existingMsg = session.Messages[msgIndex];
            updatedMsg = existingMsg with
            {
                Content = content,
                RawStream = rawStream ?? existingMsg.RawStream
            };

            var newMessages = new List<ChatMessageModel>(session.Messages);
            newMessages[msgIndex] = updatedMsg;

            updatedSession = session with
            {
                UpdatedAt = touchUpdatedAt ? DateTimeOffset.UtcNow : session.UpdatedAt,
                Messages = newMessages
            };

            _sessions[session.Id] = updatedSession;
        }

        if (updatedSession != null)
        {
            if (flushImmediately)
            {
                CancelPendingPersist(sessionId);
                _lastPersistTimes[sessionId] = DateTimeOffset.UtcNow;
                PersistSessionToDisk(updatedSession);
            }
            else
            {
                ScheduleThrottledPersist(updatedSession);
            }
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        return updatedMsg;
    }

    public void FlushSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        CancelPendingPersist(sessionId);
        var session = GetSession(sessionId);
        if (session != null)
        {
            _lastPersistTimes[sessionId] = DateTimeOffset.UtcNow;
            PersistSessionToDisk(session);
        }
    }

    private void CancelPendingPersist(string sessionId)
    {
        if (_debounceTimers.TryRemove(sessionId, out var timer))
        {
            try
            {
                timer.Dispose();
            }
            catch { }
        }
    }

    private void ScheduleThrottledPersist(ChatSessionModel session)
    {
        var sessionId = session.Id;
        var now = DateTimeOffset.UtcNow;
        if (!_lastPersistTimes.TryGetValue(sessionId, out var lastTime))
        {
            lastTime = DateTimeOffset.MinValue;
        }

        var elapsed = now - lastTime;
        if (elapsed >= TimeSpan.FromSeconds(1))
        {
            CancelPendingPersist(sessionId);
            _lastPersistTimes[sessionId] = now;
            PersistSessionToDisk(session);
            return;
        }

        var delay = TimeSpan.FromSeconds(1) - elapsed;
        if (delay < TimeSpan.FromMilliseconds(50))
        {
            delay = TimeSpan.FromMilliseconds(50);
        }

        _debounceTimers.AddOrUpdate(
            sessionId,
            id => new Timer(_ => OnDebounceTimerFired(id), null, delay, Timeout.InfiniteTimeSpan),
            (id, existingTimer) => existingTimer);
    }

    private void OnDebounceTimerFired(string sessionId)
    {
        CancelPendingPersist(sessionId);
        var session = GetSession(sessionId);
        if (session != null)
        {
            _lastPersistTimes[sessionId] = DateTimeOffset.UtcNow;
            PersistSessionToDisk(session);
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
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to persist chat session {SessionId} to disk", session.Id);
        }
    }
}

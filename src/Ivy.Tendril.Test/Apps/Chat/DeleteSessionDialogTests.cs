using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Tendril.Apps.Chat.Dialogs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class DeleteSessionDialogTests
{
    private class FakeChatHistoryService : IChatHistoryService
    {
#pragma warning disable CS0067
        public event EventHandler? SessionsChanged;
        public event EventHandler? GeneratingSessionsChanged;
#pragma warning restore CS0067

        public HashSet<string> GeneratingSessions { get; } = [];
        public HashSet<string> CompletedSessions { get; } = [];
        public List<ChatSessionModel> Sessions { get; } = [];

        public IReadOnlyList<ChatSessionModel> GetSessions() => Sessions;
        public ChatSessionModel? GetSession(string id) => Sessions.Find(s => s.Id == id);
        public ChatSessionModel CreateSession(string agentId, string modelId, string? title = null, string? effort = null)
        {
            var session = new ChatSessionModel(Guid.NewGuid().ToString(), title ?? "New Chat", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, agentId, modelId, [], effort);
            Sessions.Add(session);
            return session;
        }
        public void SaveSession(ChatSessionModel session) { }
        public void DeleteSession(string id) => Sessions.RemoveAll(s => s.Id == id);
        public void RenameSession(string id, string newTitle) { }
        public ChatMessageModel AddMessage(string sessionId, string role, string content, string? agentId = null, string? modelId = null, string? rawStream = null, string? effort = null)
        {
            return new ChatMessageModel(Guid.NewGuid().ToString(), role, content, DateTimeOffset.UtcNow, agentId, modelId, rawStream, effort);
        }
        public void SetSessionGenerating(string sessionId, bool isGenerating)
        {
            if (isGenerating) GeneratingSessions.Add(sessionId);
            else { GeneratingSessions.Remove(sessionId); CompletedSessions.Add(sessionId); }
        }
        public void ClearAllGeneratingSessions() => GeneratingSessions.Clear();
        public IReadOnlySet<string> GetGeneratingSessionIds() => GeneratingSessions;
        public IReadOnlySet<string> GetCompletedSessionIds() => CompletedSessions;
        public void ClearSessionCompleted(string sessionId) => CompletedSessions.Remove(sessionId);
        public IReadOnlyList<ChatQueuedItem> GetQueuedMessages(string sessionId) => [];
        public ChatQueuedItem EnqueueMessage(string sessionId, Ivy.Tendril.Widgets.ChatSendMessageDto dto) => new(Guid.NewGuid().ToString(), dto.Prompt, dto.Attachments, DateTimeOffset.UtcNow);
        public bool TryDequeueMessage(string sessionId, out ChatQueuedItem? item) { item = null; return false; }
        public bool RemoveQueuedMessage(string sessionId, string queueId) => false;
        public bool UpdateQueuedMessage(string sessionId, string queueId, string prompt) => false;
        public void ClearQueuedMessages(string sessionId) { }
        public void AddSpawnedJob(string sessionId, string jobId) { }
        public IReadOnlyList<string> GetSpawnedJobs(string sessionId) => [];
    }

    private class TestState<T> : IState<T>
    {
        private readonly T _initial;

        public TestState(T initial)
        {
            _initial = initial;
            Value = initial;
        }

        public T Value { get; set; }

        public IDisposable Subscribe(IObserver<T> observer) => throw new NotImplementedException();
        public void Dispose() { }
        public T Set(T value) => Value = value;
        public T Set(Func<T, T> setter) => Value = setter(Value);
        public T Reset() => Value = _initial;
        public IDisposable SubscribeAny(Action action) => throw new NotImplementedException();
        public IDisposable SubscribeAny(Action<object?> action) => throw new NotImplementedException();
        public Type GetStateType() => typeof(T);
        public object? GetValueAsObject() => Value;
        public IEffectTrigger ToTrigger() => throw new NotImplementedException();
    }

    [Fact]
    public void Build_ReturnsNull_WhenDeletingSessionIdIsNull()
    {
        var service = new FakeChatHistoryService();
        var deletingSessionId = new TestState<string?>(null);
        var activeSessionId = new TestState<string?>("sess-1");
        var sessionVersion = new TestState<int>(1);

        var dialog = new DeleteSessionDialog(deletingSessionId, null, service, activeSessionId, sessionVersion);
        var result = dialog.Build();

        Assert.Null(result);
    }

    [Fact]
    public void Build_ReturnsDialog_WhenDeletingSessionIdIsSet()
    {
        var service = new FakeChatHistoryService();
        var session = new ChatSessionModel("sess-1", "My Chat", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []);
        service.Sessions.Add(session);

        var deletingSessionId = new TestState<string?>("sess-1");
        var activeSessionId = new TestState<string?>("sess-1");
        var sessionVersion = new TestState<int>(1);

        var dialog = new DeleteSessionDialog(deletingSessionId, session, service, activeSessionId, sessionVersion);
        var result = dialog.Build();

        Assert.NotNull(result);
        var dialogWidget = Assert.IsType<Dialog>(result);
        Assert.Equal(3, dialogWidget.Children.Length);
        Assert.IsType<DialogHeader>(dialogWidget.Children[0]);
        Assert.IsType<DialogBody>(dialogWidget.Children[1]);
        Assert.IsType<DialogFooter>(dialogWidget.Children[2]);
    }

    [Fact]
    public void Build_HandlesNullTitle_WithFallbackText()
    {
        var service = new FakeChatHistoryService();
        var session = new ChatSessionModel("sess-1", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []);
        service.Sessions.Add(session);

        var deletingSessionId = new TestState<string?>("sess-1");
        var activeSessionId = new TestState<string?>("sess-1");
        var sessionVersion = new TestState<int>(1);

        var dialog = new DeleteSessionDialog(deletingSessionId, session, service, activeSessionId, sessionVersion);
        var result = dialog.Build();

        Assert.NotNull(result);
        Assert.IsType<Dialog>(result);
    }

    [Fact]
    public async Task Dialog_OnClose_ResetsDeletingSessionId()
    {
        var service = new FakeChatHistoryService();
        var session = new ChatSessionModel("sess-1", "My Chat", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []);
        service.Sessions.Add(session);

        var deletingSessionId = new TestState<string?>("sess-1");
        var activeSessionId = new TestState<string?>("sess-1");
        var sessionVersion = new TestState<int>(1);

        var dialog = new DeleteSessionDialog(deletingSessionId, session, service, activeSessionId, sessionVersion);
        var result = Assert.IsType<Dialog>(dialog.Build());

        Assert.NotNull(result.OnClose);
        await result.OnClose.Invoke(new Event<Dialog>("onClose", result));

        Assert.Null(deletingSessionId.Value);
    }

    [Fact]
    public async Task Dialog_CancelButton_ClosesDialogWithoutDeletingSession()
    {
        var service = new FakeChatHistoryService();
        var session = new ChatSessionModel("sess-1", "My Chat", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []);
        service.Sessions.Add(session);

        var deletingSessionId = new TestState<string?>("sess-1");
        var activeSessionId = new TestState<string?>("sess-1");
        var sessionVersion = new TestState<int>(1);

        var dialog = new DeleteSessionDialog(deletingSessionId, session, service, activeSessionId, sessionVersion);
        var result = Assert.IsType<Dialog>(dialog.Build());

        var footer = Assert.IsType<DialogFooter>(result.Children[2]);
        var cancelBtn = Assert.IsType<Button>(footer.Children[0]);
        Assert.Equal("Cancel", cancelBtn.Title);

        Assert.NotNull(cancelBtn.OnClick);
        await cancelBtn.OnClick.Invoke(new Event<Button>("click", cancelBtn));

        Assert.Null(deletingSessionId.Value);
        Assert.Single(service.Sessions);
        Assert.Equal(1, sessionVersion.Value);
        Assert.Equal("sess-1", activeSessionId.Value);
    }

    [Fact]
    public async Task Dialog_DeleteButton_DeletesSession_IncrementsVersion_UpdatesActiveSession()
    {
        var service = new FakeChatHistoryService();
        var session1 = new ChatSessionModel("sess-1", "Session 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []);
        var session2 = new ChatSessionModel("sess-2", "Session 2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []);
        service.Sessions.Add(session1);
        service.Sessions.Add(session2);

        var deletingSessionId = new TestState<string?>("sess-1");
        var activeSessionId = new TestState<string?>("sess-1");
        var sessionVersion = new TestState<int>(1);

        var dialog = new DeleteSessionDialog(deletingSessionId, session1, service, activeSessionId, sessionVersion);
        var result = Assert.IsType<Dialog>(dialog.Build());

        var footer = Assert.IsType<DialogFooter>(result.Children[2]);
        var deleteBtn = Assert.IsType<Button>(footer.Children[1]);
        Assert.Equal("Delete", deleteBtn.Title);

        Assert.NotNull(deleteBtn.OnClick);
        await deleteBtn.OnClick.Invoke(new Event<Button>("click", deleteBtn));

        Assert.Null(deletingSessionId.Value);
        Assert.DoesNotContain(service.Sessions, s => s.Id == "sess-1");
        Assert.Single(service.Sessions);
        Assert.Equal(2, sessionVersion.Value);
        Assert.Equal("sess-2", activeSessionId.Value);
    }

    [Fact]
    public async Task Dialog_DeleteButton_WhenDeletingNonActiveSession_PreservesActiveSessionId()
    {
        var service = new FakeChatHistoryService();
        var session1 = new ChatSessionModel("sess-1", "Session 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []);
        var session2 = new ChatSessionModel("sess-2", "Session 2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []);
        service.Sessions.Add(session1);
        service.Sessions.Add(session2);

        var deletingSessionId = new TestState<string?>("sess-2");
        var activeSessionId = new TestState<string?>("sess-1");
        var sessionVersion = new TestState<int>(1);

        var dialog = new DeleteSessionDialog(deletingSessionId, session2, service, activeSessionId, sessionVersion);
        var result = Assert.IsType<Dialog>(dialog.Build());

        var footer = Assert.IsType<DialogFooter>(result.Children[2]);
        var deleteBtn = Assert.IsType<Button>(footer.Children[1]);

        Assert.NotNull(deleteBtn.OnClick);
        await deleteBtn.OnClick.Invoke(new Event<Button>("click", deleteBtn));

        Assert.Null(deletingSessionId.Value);
        Assert.DoesNotContain(service.Sessions, s => s.Id == "sess-2");
        Assert.Single(service.Sessions);
        Assert.Equal(2, sessionVersion.Value);
        Assert.Equal("sess-1", activeSessionId.Value);
    }
}

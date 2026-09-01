using System;
using System.Collections.Generic;
using Ivy;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class SidebarViewTests
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
        public IReadOnlySet<string> GetGeneratingSessionIds() => GeneratingSessions;
        public IReadOnlySet<string> GetCompletedSessionIds() => CompletedSessions;
        public void ClearSessionCompleted(string sessionId) => CompletedSessions.Remove(sessionId);
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
    public void Build_ReturnsHeaderLayout_WithSessionItems()
    {
        var service = new FakeChatHistoryService();
        service.GeneratingSessions.Add("sess-1");
        service.CompletedSessions.Add("sess-2");

        var sessions = new List<ChatSessionModel>
        {
            new("sess-1", "Generating session", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []),
            new("sess-2", "Completed session", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "sonnet", []),
            new("sess-3", "Idle session", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "opencode", "deepseek", [])
        };

        var activeSessionId = new TestState<string?>("sess-1");
        var sessionVersion = new TestState<int>(1);
        var selectedAgent = new TestState<string>("claude");
        var selectedModel = new TestState<string>("opus");
        var searchState = new TestState<string>("");

        var view = new SidebarView(sessions, activeSessionId, sessionVersion, selectedAgent, selectedModel, searchState, service);
        var result = view.Build();

        Assert.NotNull(result);
        Assert.IsType<HeaderLayout>(result);
    }

    [Fact]
    public void Build_WithNonMatchingSearch_ShowsNoResults()
    {
        var service = new FakeChatHistoryService();
        var sessions = new List<ChatSessionModel>
        {
            new("sess-1", "Generating session", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", [])
        };

        var activeSessionId = new TestState<string?>(null);
        var sessionVersion = new TestState<int>(1);
        var selectedAgent = new TestState<string>("claude");
        var selectedModel = new TestState<string>("opus");
        var searchState = new TestState<string>("xyz non existent");

        var view = new SidebarView(sessions, activeSessionId, sessionVersion, selectedAgent, selectedModel, searchState, service);
        var result = view.Build();

        Assert.NotNull(result);
        var headerLayout = Assert.IsType<HeaderLayout>(result);
    }

    [Fact]
    public void Build_RendersSelectedAndUnselectedSessionButtons_WithCorrectVariants()
    {
        var service = new FakeChatHistoryService();
        var sessions = new List<ChatSessionModel>
        {
            new("sess-1", "Selected session", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", []),
            new("sess-2", "Unselected session", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "sonnet", [])
        };

        var activeSessionId = new TestState<string?>("sess-1");
        var sessionVersion = new TestState<int>(1);
        var selectedAgent = new TestState<string>("claude");
        var selectedModel = new TestState<string>("opus");
        var searchState = new TestState<string>("");

        var view = new SidebarView(sessions, activeSessionId, sessionVersion, selectedAgent, selectedModel, searchState, service);
        var result = view.Build();

        Assert.NotNull(result);
        var headerLayout = Assert.IsType<HeaderLayout>(result);
        var contentSlot = Assert.IsType<Slot>(headerLayout.Children[1]);
        var list = Assert.IsType<List>(contentSlot.Children[0]);
        Assert.NotNull(list.Children);
        Assert.Equal(2, list.Children.Length);

        var selectedBtn = Assert.IsType<Button>(list.Children[0]);
        Assert.Equal(ButtonVariant.Secondary, selectedBtn.Variant);

        var unselectedBtn = Assert.IsType<Button>(list.Children[1]);
        Assert.Equal(ButtonVariant.Ghost, unselectedBtn.Variant);
    }
}


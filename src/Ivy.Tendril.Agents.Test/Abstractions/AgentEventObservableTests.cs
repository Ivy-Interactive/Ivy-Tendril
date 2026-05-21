using System.Reactive.Subjects;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class AgentEventObservableTests
{
    private readonly ReplaySubject<AgentEvent> _subject = new();

    [Fact]
    public void OnText_FiltersTextEvents()
    {
        var received = new List<TextEvent>();
        using var sub = _subject.OnText(e => received.Add(e));

        _subject.OnNext(new ThinkingEvent { Kind = AgentEventKind.Thinking, Content = "thinking" });
        _subject.OnNext(new TextEvent { Kind = AgentEventKind.Text, Text = "hello" });
        _subject.OnNext(new TextEvent { Kind = AgentEventKind.Text, Text = "world" });
        _subject.OnCompleted();

        Assert.Equal(2, received.Count);
        Assert.Equal("hello", received[0].Text);
        Assert.Equal("world", received[1].Text);
    }

    [Fact]
    public void OnThinking_FiltersThinkingEvents()
    {
        var received = new List<ThinkingEvent>();
        using var sub = _subject.OnThinking(e => received.Add(e));

        _subject.OnNext(new TextEvent { Kind = AgentEventKind.Text, Text = "text" });
        _subject.OnNext(new ThinkingEvent { Kind = AgentEventKind.Thinking, Content = "thought" });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.Equal("thought", received[0].Content);
    }

    [Fact]
    public void OnToolCall_FiltersToolCallEvents()
    {
        var received = new List<ToolCallEvent>();
        using var sub = _subject.OnToolCall(e => received.Add(e));

        _subject.OnNext(new ToolCallEvent { Kind = AgentEventKind.ToolCall, ToolUseId = "t1", ToolName = "Read" });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.Equal("Read", received[0].ToolName);
    }

    [Fact]
    public void OnToolResult_FiltersToolResultEvents()
    {
        var received = new List<ToolResultEvent>();
        using var sub = _subject.OnToolResult(e => received.Add(e));

        _subject.OnNext(new ToolResultEvent { Kind = AgentEventKind.ToolResult, ToolUseId = "t1", Output = "content" });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.Equal("content", received[0].Output);
    }

    [Fact]
    public void OnError_FiltersErrorEvents()
    {
        var received = new List<ErrorEvent>();
        using var sub = _subject.OnError(e => received.Add(e));

        _subject.OnNext(new ErrorEvent { Kind = AgentEventKind.Error, Message = "oops" });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.Equal("oops", received[0].Message);
    }

    [Fact]
    public void OnResult_FiltersResultEvents()
    {
        var received = new List<ResultEvent>();
        using var sub = _subject.OnResult(e => received.Add(e));

        _subject.OnNext(new TextEvent { Kind = AgentEventKind.Text, Text = "x" });
        _subject.OnNext(new ResultEvent { Kind = AgentEventKind.Result, IsSuccess = true, Response = "done" });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.True(received[0].IsSuccess);
    }

    [Fact]
    public void OnFileChange_FiltersFileChangeEvents()
    {
        var received = new List<FileChangeEvent>();
        using var sub = _subject.OnFileChange(e => received.Add(e));

        _subject.OnNext(new FileChangeEvent
        {
            Kind = AgentEventKind.FileChange,
            FilePath = "/tmp/test.txt",
            ChangeKind = FileChangeKind.Created,
            LinesAdded = 10,
        });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.Equal("/tmp/test.txt", received[0].FilePath);
        Assert.Equal(FileChangeKind.Created, received[0].ChangeKind);
    }

    [Fact]
    public void OnPermissionRequest_FiltersPermissionRequestEvents()
    {
        var received = new List<PermissionRequestEvent>();
        using var sub = _subject.OnPermissionRequest(e => received.Add(e));

        _subject.OnNext(new PermissionRequestEvent
        {
            Kind = AgentEventKind.PermissionRequest,
            RequestId = "r1",
            ToolName = "Bash",
            IsDestructive = true,
        });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.True(received[0].IsDestructive);
    }

    [Fact]
    public void OnUserQuestion_FiltersUserQuestionEvents()
    {
        var received = new List<UserQuestionEvent>();
        using var sub = _subject.OnUserQuestion(e => received.Add(e));

        _subject.OnNext(new UserQuestionEvent
        {
            Kind = AgentEventKind.UserQuestion,
            QuestionId = "q1",
            Question = "Pick one?",
            Options = [new QuestionOption { Label = "A", Value = "a" }],
        });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.Equal("Pick one?", received[0].Question);
    }

    [Fact]
    public void OnSessionCompleted_FiltersSessionCompletedEvents()
    {
        var received = new List<SessionCompletedEvent>();
        using var sub = _subject.OnSessionCompleted(e => received.Add(e));

        _subject.OnNext(new SessionCompletedEvent
        {
            Kind = AgentEventKind.SessionCompleted,
            SessionId = "s1",
            AgentId = AgentId.Claude,
            FinalState = SessionState.Completed,
            Result = new ResultEvent { Kind = AgentEventKind.Result, IsSuccess = true },
        });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.Equal(SessionState.Completed, received[0].FinalState);
    }

    [Fact]
    public void Dispose_UnsubscribesCleanly()
    {
        var received = new List<TextEvent>();
        var sub = _subject.OnText(e => received.Add(e));

        _subject.OnNext(new TextEvent { Kind = AgentEventKind.Text, Text = "before" });
        sub.Dispose();
        _subject.OnNext(new TextEvent { Kind = AgentEventKind.Text, Text = "after" });
        _subject.OnCompleted();

        Assert.Single(received);
        Assert.Equal("before", received[0].Text);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test;

/// <summary>
/// Covers the streaming-turn half of plan 00457: the live in-progress assistant message must be
/// discoverable by id (<see cref="IChatExecutionService.GetStreamingMessageId"/>) and answers submitted
/// while it is still streaming must land in the in-memory buffer so the periodic persist tick (and the
/// final flush on completion/cancellation) don't clobber them.
/// </summary>
public class ChatExecutionServiceQuestionAnswersTests
{
    private sealed class TestSession : IAgentSession
    {
        private readonly System.Reactive.Subjects.Subject<AgentEvent> _events = new();
        private readonly TaskCompletionSource<ResultEvent> _completion = new();

        public string SessionId { get; } = "sess-questions-test";
        public string AgentId { get; } = "codex";
        public SessionState State => SessionState.Running;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? CompletedAt => null;
        public SessionMetadata? Metadata => null;
        public IObservable<AgentEvent> Events => _events;
        public IObservable<string>? RawOutput => null;
        public IObservable<string>? RawStderr => null;
        public ResultEvent? Result => null;
        public bool SupportsPermissionResponse => false;
        public bool SupportsQuestionResponse => false;
        public bool SupportsMultiTurn => false;

        public bool HasObservers => _events.HasObservers;

        public void Emit(AgentEvent evt) => _events.OnNext(evt);

        public void Complete(ResultEvent result) => _completion.TrySetResult(result);

        public async Task<ResultEvent> WaitForCompletionAsync(CancellationToken ct = default)
        {
            using var reg = ct.Register(() => _completion.TrySetCanceled(ct));
            return await _completion.Task;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _completion.TrySetCanceled();
            return Task.CompletedTask;
        }

        public Task KillAsync()
        {
            _completion.TrySetCanceled();
            return Task.CompletedTask;
        }

        public Task RespondToPermissionAsync(string requestId, PermissionDecision decision, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RespondToQuestionAsync(string questionId, QuestionResponse response, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendFollowUpAsync(string message, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestAgentRunnerWrapper : IAgentRunner
    {
        private readonly IAgentRunner _inner;
        private readonly IAgentSession _session;

        public TestAgentRunnerWrapper(IAgentRunner inner, IAgentSession session)
        {
            _inner = inner;
            _session = session;
        }

        public Task<IAgentSession> LaunchAsync(AgentResolutionContext context, CancellationToken ct = default)
            => Task.FromResult(_session);

        public Task<ResultEvent> RunToCompletionAsync(AgentResolutionContext context, CancellationToken ct = default)
            => _inner.RunToCompletionAsync(context, ct);

        public IReadOnlyList<IAgentSession> ActiveSessions => _inner.ActiveSessions;
        public IObservable<IAgentSession> Sessions => _inner.Sessions;
        public Task StopAllAsync(CancellationToken ct = default) => _inner.StopAllAsync(ct);
        public IReadOnlyList<string> RegisteredAgents => _inner.RegisteredAgents;
        public IAgentCli GetCli(string agentId) => _inner.GetCli(agentId);
        public IEventParser GetParser(string agentId) => _inner.GetParser(agentId);
        public IAgentHealthCheck GetHealthCheck(string agentId) => _inner.GetHealthCheck(agentId);
        public IAgentDescriptor GetDescriptor(string agentId) => _inner.GetDescriptor(agentId);
        public IFailureAnalyzer? GetFailureAnalyzer(string agentId) => _inner.GetFailureAnalyzer(agentId);
        public ISessionCostParser? GetCostParser(string agentId) => _inner.GetCostParser(agentId);
        public IAgentPty? GetPty(string agentId) => _inner.GetPty(agentId);
        public IModelCatalogProvider? GetModelCatalog(string agentId) => _inner.GetModelCatalog(agentId);
        public IEnumerable<IModelCatalogProvider> ModelCatalogs => _inner.ModelCatalogs;
    }

    private static (ChatExecutionService ExecService, ChatHistoryService ChatService, TestSession Session, string TempDir) CreateHarness()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilQuestionAnswersTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var configService = new ConfigService(new TendrilSettings { CodingAgent = "codex" }, tempDir);
        var chatService = new ChatHistoryService(configService);
        var session = new TestSession();
        var agentRunner = new TestAgentRunnerWrapper(TestAgentRunner.Create(), session);
        var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);

        var execService = new ChatExecutionService(
            configService,
            chatService,
            agentRunner,
            namingService,
            new JsonEventSerializer());

        return (execService, chatService, session, tempDir);
    }

    private static async Task WaitUntilGeneratingAndSubscribed(ChatExecutionService execService, TestSession session, string sessionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while ((!execService.IsGenerating(sessionId) || !session.HasObservers) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        Assert.True(execService.IsGenerating(sessionId));
        Assert.True(session.HasObservers);
    }

    [Fact]
    public async Task GetStreamingMessageId_ReturnsTheInProgressAssistantMessageId_ThenNullOnceItSettles()
    {
        var (execService, chatService, session, tempDir) = CreateHarness();
        try
        {
            var chatSession = chatService.CreateSession("codex", "gpt-5.6-sol");
            var sendTask = execService.SendMessageAsync(chatSession.Id, "Where should we deploy?");

            await WaitUntilGeneratingAndSubscribed(execService, session, chatSession.Id);

            var streamingMessageId = execService.GetStreamingMessageId(chatSession.Id);
            Assert.NotNull(streamingMessageId);

            var assistantMsg = chatService.GetSession(chatSession.Id)?.Messages
                .FirstOrDefault(m => m.Role == "assistant");
            Assert.NotNull(assistantMsg);
            Assert.Equal(assistantMsg!.Id, streamingMessageId);

            await execService.CancelAsync(chatSession.Id);

            Assert.Null(execService.GetStreamingMessageId(chatSession.Id));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void GetStreamingMessageId_WithNoActiveExecution_ReturnsNull()
    {
        var (execService, chatService, _, tempDir) = CreateHarness();
        try
        {
            var chatSession = chatService.CreateSession("codex", "gpt-5.6-sol");
            Assert.Null(execService.GetStreamingMessageId(chatSession.Id));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ApplyQuestionAnswers_PatchesTheLiveBufferSoTheAnswerSurvivesTheFinalFlush()
    {
        var (execService, chatService, session, tempDir) = CreateHarness();
        try
        {
            var chatSession = chatService.CreateSession("codex", "gpt-5.6-sol");
            var sendTask = execService.SendMessageAsync(chatSession.Id, "Where should we deploy?");

            await WaitUntilGeneratingAndSubscribed(execService, session, chatSession.Id);

            const string questionsText =
                "Which env?\n\n```questions\n- id: target_env\n  title: Which env?\n  options:\n    - title: Staging\n      value: staging\n```\n";
            session.Emit(new TextEvent
            {
                Kind = AgentEventKind.Text,
                Text = questionsText,
                IsDelta = false,
                Timestamp = DateTimeOffset.UtcNow
            });

            // The event handler runs synchronously off the Rx subject, but give it a moment either way.
            var bufferedDeadline = DateTime.UtcNow.AddSeconds(2);
            while (!execService.GetStreamSnapshot(chatSession.Id).Contains("target_env") && DateTime.UtcNow < bufferedDeadline)
            {
                await Task.Delay(20);
            }
            Assert.Contains("target_env", execService.GetStreamSnapshot(chatSession.Id));

            var answers = new Dictionary<string, string[]>
            {
                ["target_env"] = ["staging"]
            };
            execService.ApplyQuestionAnswers(chatSession.Id, answers);

            // The submitted answer must be visible in the live buffer immediately, before any persist tick runs.
            Assert.Contains("answer: staging", execService.GetStreamSnapshot(chatSession.Id));

            // Ending the turn flushes the live buffer into the persisted message; the answer must not be
            // reverted by that flush, which is exactly the bug this plan fixes.
            await execService.CancelAsync(chatSession.Id);

            var assistantMsg = chatService.GetSession(chatSession.Id)?.Messages
                .FirstOrDefault(m => m.Role == "assistant");
            Assert.NotNull(assistantMsg);
            Assert.Contains("answer: staging", assistantMsg!.Content);
            Assert.NotNull(assistantMsg.RawStream);
            Assert.Contains("answer: staging", assistantMsg.RawStream);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ApplyQuestionAnswers_WithNoActiveExecution_DoesNotThrow()
    {
        var (execService, chatService, _, tempDir) = CreateHarness();
        try
        {
            var chatSession = chatService.CreateSession("codex", "gpt-5.6-sol");
            var answers = new Dictionary<string, string[]> { ["target_env"] = ["staging"] };

            execService.ApplyQuestionAnswers(chatSession.Id, answers);

            await Task.CompletedTask;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test;

public class ChatExecutionServiceJobTrackingTests
{
    private class FakeChatJobService : IJobService
    {
        public Dictionary<string, string> JobChatSessions { get; } = new();
        public List<JobItem> Jobs { get; } = new();

        public event Action<JobItem>? JobFinished;
#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067

        public void SetChatSessionId(string id, string chatSessionId)
        {
            JobChatSessions[id] = chatSessionId;
        }

        public void FireJobFinished(JobItem job)
        {
            JobFinished?.Invoke(job);
        }

        public string StartJob(JobArgsBase args, string? inboxFilePath = null) => "job-001";
        public void ForceStartJob(string id) { }
        public bool IsInboxFileTracked(string filePath) => false;
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public int StopAllJobs() => 0;
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public int StopQueuedJobs() => 0;
        public List<JobItem> GetJobs() => Jobs;
        public List<JobItem> GetJobsForPlan(string planFile) => new();
        public JobItem? GetJob(string id) => Jobs.FirstOrDefault(j => j.Id == id);
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => false;
        public bool ReportJobFailure(string id, string message) => false;
        public void Dispose() { }
    }

    [Fact]
    public void ToolResultEvent_WithJobStarted_TracksSpawnedJobAndSetsChatSessionId()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilTrackJobTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            var fakeJobService = new FakeChatJobService();

            var execService = new ChatExecutionService(
                configService,
                chatService,
                agentRunner,
                namingService,
                serializer,
                logger: null,
                serviceProvider: null,
                jobService: fakeJobService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");

            execService.TryTrackSpawnedJob(session.Id, "Started agent successfully. Job started: 00148\nTracking progress...");

            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.NotNull(updatedSession.SpawnedJobIds);
            Assert.Contains("00148", updatedSession.SpawnedJobIds);

            Assert.True(fakeJobService.JobChatSessions.ContainsKey("00148"));
            Assert.Equal(session.Id, fakeJobService.JobChatSessions["00148"]);
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
    public void ToolResultEvent_WithJobIdDiscussion_DoesNotTrackSpawnedJob()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilTrackJobIdTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            var fakeJobService = new FakeChatJobService();

            var execService = new ChatExecutionService(
                configService,
                chatService,
                agentRunner,
                namingService,
                serializer,
                logger: null,
                serviceProvider: null,
                jobService: fakeJobService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");

            // Discussions and listings mentioning Job ID: should NOT track as newly spawned jobs
            execService.TryTrackSpawnedJob(session.Id, "Here are the jobs:\n- **Job ID**: test-job-999\n- Job ID: 00151");

            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Null(updatedSession.SpawnedJobIds);
            Assert.Empty(fakeJobService.JobChatSessions);
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
    public void RemoveSpawnedJobs_PrunesStaleJobsFromSession()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilRemoveSpawnedTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");
            chatService.AddSpawnedJob(session.Id, "00151");
            chatService.AddSpawnedJob(session.Id, "00152");
            chatService.AddSpawnedJob(session.Id, "00153");

            var sess = chatService.GetSession(session.Id);
            Assert.NotNull(sess?.SpawnedJobIds);
            Assert.Equal(3, sess.SpawnedJobIds.Count);

            chatService.RemoveSpawnedJobs(session.Id, new[] { "00151", "00152" });

            var updated = chatService.GetSession(session.Id);
            Assert.NotNull(updated?.SpawnedJobIds);
            Assert.Single(updated.SpawnedJobIds);
            Assert.Equal("00153", updated.SpawnedJobIds[0]);
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
    public async Task JobFinished_WhenSpawnedFromChat_DispatchesSystemEventMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilJobFinishedChatTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            var fakeJobService = new FakeChatJobService();

            var execService = new ChatExecutionService(
                configService,
                chatService,
                agentRunner,
                namingService,
                serializer,
                logger: null,
                serviceProvider: null,
                jobService: fakeJobService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");
            chatService.AddSpawnedJob(session.Id, "job-777");

            var completedJob = new JobItem
            {
                Id = "job-777",
                Type = "ExecutePlan",
                ChatSessionId = session.Id,
                Status = JobStatus.Completed,
                ReportedPlanId = "P-100",
                ReportedPlanTitle = "Implement Feature X"
            };

            // Fire JobFinished
            fakeJobService.FireJobFinished(completedJob);

            // Wait briefly for async Task.Run in OnJobFinished to record message
            for (int i = 0; i < 20; i++)
            {
                var s = chatService.GetSession(session.Id);
                if (s?.Messages.Any(m => m.Role == "system") == true)
                    break;
                await Task.Delay(50);
            }

            var sess = chatService.GetSession(session.Id);
            Assert.NotNull(sess);
            var systemMsg = sess.Messages.FirstOrDefault(m => m.Role == "system");
            Assert.NotNull(systemMsg);
            Assert.Contains("[System Event]", systemMsg.Content);
            Assert.Contains("job-777", systemMsg.Content);
            Assert.Contains("Implement Feature X", systemMsg.Content);
            Assert.Contains("Completed successfully", systemMsg.Content);

            // Verify deduplication: firing again does not add a second system event message
            var countBefore = sess.Messages.Count(m => m.Role == "system");
            fakeJobService.FireJobFinished(completedJob);
            await Task.Delay(100);

            var sessAfter = chatService.GetSession(session.Id);
            Assert.Equal(countBefore, sessAfter!.Messages.Count(m => m.Role == "system"));
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
    public async Task JobFinished_WhenJobNotSpawnedInChat_DoesNotDispatchMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilJobFinishedUnrelatedTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            var fakeJobService = new FakeChatJobService();

            var execService = new ChatExecutionService(
                configService,
                chatService,
                agentRunner,
                namingService,
                serializer,
                logger: null,
                serviceProvider: null,
                jobService: fakeJobService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");

            var unrelatedJob = new JobItem
            {
                Id = "job-unrelated-888",
                Type = "ExecutePlan",
                ChatSessionId = null,
                Status = JobStatus.Completed,
                ReportedPlanId = "P-200",
                ReportedPlanTitle = "Background Work"
            };

            fakeJobService.FireJobFinished(unrelatedJob);
            await Task.Delay(150);

            var sess = chatService.GetSession(session.Id);
            Assert.NotNull(sess);
            Assert.DoesNotContain(sess.Messages, m => m.Role == "system");
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
    public async Task SystemEvent_WhenExecutionRunning_NeverEnqueuesIntoUserQueuedMessages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilNoQueueTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            var fakeJobService = new FakeChatJobService();

            var execService = new ChatExecutionService(
                configService,
                chatService,
                agentRunner,
                namingService,
                serializer,
                logger: null,
                serviceProvider: null,
                jobService: fakeJobService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");

            // Simulate that the session is currently generating / executing
            var activeExecutionsField = typeof(ChatExecutionService).GetField("_activeExecutions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(activeExecutionsField);

            var cts = new System.Threading.CancellationTokenSource();
            var activeExecType = typeof(ChatExecutionService).GetNestedType("ActiveChatExecution",
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(activeExecType);
            var activeExec = Activator.CreateInstance(activeExecType, cts);
            Assert.NotNull(activeExec);

            var dict = (System.Collections.IDictionary)activeExecutionsField.GetValue(execService)!;
            dict[session.Id] = activeExec;

            // Send a system event while session is active
            await execService.SendMessageAsync(session.Id, "[System Event] Job 00151 finished with status: Stopped", role: "system");

            // Verify user queue in chat history has ZERO items
            var queued = chatService.GetQueuedMessages(session.Id);
            Assert.Empty(queued);

            // Clean up
            dict.Remove(session.Id);
            cts.Dispose();
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
    public void GetQueuedMessages_PurgesAnySystemEventMessages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilPurgeQueueTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");

            // Manually enqueue a user message and a rogue system event
            chatService.EnqueueMessage(session.Id, new ChatSendMessageDto("User prompt", null, session.Id));
            chatService.EnqueueMessage(session.Id, new ChatSendMessageDto("[System Event] Job 00151 finished", null, session.Id));

            var queued = chatService.GetQueuedMessages(session.Id);
            Assert.Single(queued);
            Assert.Equal("User prompt", queued[0].Prompt);
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
    public async Task ForceSendMessageAsync_WhenExecutionRunning_InterruptsCurrentExecutionAndPreservesQueue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilForceSendTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var agentRunner = TestAgentRunner.Create();
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            var fakeJobService = new FakeChatJobService();

            var execService = new ChatExecutionService(
                configService,
                chatService,
                agentRunner,
                namingService,
                serializer,
                logger: null,
                serviceProvider: null,
                jobService: fakeJobService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");

            // Enqueue two messages: one that will be force sent, and one that should remain in queue
            chatService.EnqueueMessage(session.Id, new ChatSendMessageDto("Force sent prompt", null, session.Id));
            chatService.EnqueueMessage(session.Id, new ChatSendMessageDto("Should stay queued", null, session.Id));

            // Simulate an active running execution
            var activeExecutionsField = typeof(ChatExecutionService).GetField("_activeExecutions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(activeExecutionsField);

            var cts = new System.Threading.CancellationTokenSource();
            var activeExecType = typeof(ChatExecutionService).GetNestedType("ActiveChatExecution",
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(activeExecType);
            var activeExec = Activator.CreateInstance(activeExecType, cts);
            Assert.NotNull(activeExec);

            var tcs = new TaskCompletionSource();
            var execTaskProp = activeExecType.GetProperty("ExecutionTask");
            execTaskProp?.SetValue(activeExec, tcs.Task);

            var dict = (System.Collections.IDictionary)activeExecutionsField.GetValue(execService)!;
            dict[session.Id] = activeExec;

            Assert.True(execService.IsGenerating(session.Id));

            // Run InterruptAsync on the session
            var interruptTask = execService.InterruptAsync(session.Id);

            // Verify cancellation was requested on the running execution's CTS
            await Task.Delay(50);
            Assert.True(cts.IsCancellationRequested);

            // Complete the interrupted task
            tcs.SetResult();
            await interruptTask;

            // Execution should no longer be active
            Assert.False(execService.IsGenerating(session.Id));

            // Verify other queued message is still in queue
            var queued = chatService.GetQueuedMessages(session.Id);
            Assert.Contains(queued, q => q.Prompt == "Should stay queued");

            cts.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    private sealed class CancellableTestSession : IAgentSession
    {
        private readonly System.Reactive.Subjects.Subject<AgentEvent> _events = new();
        private readonly TaskCompletionSource<ResultEvent> _completion = new();

        public string SessionId { get; } = "sess-cancel-test";
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

    private class CancellableAgentRunner : IAgentRunner
    {
        private readonly IAgentRunner _inner;
        private readonly IAgentSession _session;

        public CancellableAgentRunner(IAgentRunner inner, IAgentSession session)
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

    [Fact]
    public async Task CancelAsync_WhenCancelledDuringExecution_PreservesRawHistoryAndAppendsCancellationMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilCancelHistoryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var cancellableSession = new CancellableTestSession();
            var agentRunner = new CancellableAgentRunner(TestAgentRunner.Create(), cancellableSession);
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            var fakeJobService = new FakeChatJobService();

            var execService = new ChatExecutionService(
                configService,
                chatService,
                agentRunner,
                namingService,
                serializer,
                logger: null,
                serviceProvider: null,
                jobService: fakeJobService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");

            // Start execution
            var sendTask = execService.SendMessageAsync(session.Id, "Analyze the project");

            // Wait until execution is active and subscribed to session events
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while ((!execService.IsGenerating(session.Id) || !cancellableSession.HasObservers) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
            Assert.True(execService.IsGenerating(session.Id));
            Assert.True(cancellableSession.HasObservers);

            // Emit some events: thinking, tool call + result, open tool call, text
            cancellableSession.Emit(new ThinkingEvent
            {
                Kind = AgentEventKind.Thinking,
                Content = "Searching codebase for entry points",
                Timestamp = DateTimeOffset.UtcNow
            });
            cancellableSession.Emit(new ToolCallEvent
            {
                Kind = AgentEventKind.ToolCall,
                ToolUseId = "tool-1",
                ToolName = "Grep",
                InputJson = "{\"pattern\":\"Main\"}",
                Timestamp = DateTimeOffset.UtcNow
            });
            cancellableSession.Emit(new ToolResultEvent
            {
                Kind = AgentEventKind.ToolResult,
                ToolUseId = "tool-1",
                Output = "Program.cs:5: static void Main()",
                IsError = false,
                Timestamp = DateTimeOffset.UtcNow
            });
            cancellableSession.Emit(new ToolCallEvent
            {
                Kind = AgentEventKind.ToolCall,
                ToolUseId = "tool-2",
                ToolName = "Bash",
                InputJson = "{\"command\":\"dotnet test\"}",
                Timestamp = DateTimeOffset.UtcNow
            });
            cancellableSession.Emit(new TextEvent
            {
                Kind = AgentEventKind.Text,
                Text = "I found the main entry point.",
                IsDelta = false,
                Timestamp = DateTimeOffset.UtcNow
            });

            // Wait briefly for events to be received by the subscription
            await Task.Delay(100);

            // Cancel the execution
            await execService.CancelAsync(session.Id);

            // Ensure generating flag is cleared
            Assert.False(execService.IsGenerating(session.Id));

            // Verify the assistant message in chat history
            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            var assistantMsg = updatedSession.Messages.FirstOrDefault(m => m.Role == "assistant");
            Assert.NotNull(assistantMsg);

            // Content should append cancellation message to prior collected text
            Assert.Contains("I found the main entry point.", assistantMsg.Content);
            Assert.Contains("Execution was cancelled.", assistantMsg.Content);

            // RawStream must be preserved (not null) and contain history + cancellation
            Assert.NotNull(assistantMsg.RawStream);
            Assert.Contains("Searching codebase for entry points", assistantMsg.RawStream);
            Assert.Contains("tool-1", assistantMsg.RawStream);
            Assert.Contains("Program.cs:5", assistantMsg.RawStream);
            Assert.Contains("tool-2", assistantMsg.RawStream);
            // Open tool-2 was cancelled
            Assert.Contains("[Cancelled]", assistantMsg.RawStream);
            Assert.Contains("I found the main entry point.", assistantMsg.RawStream);
            // Appended cancellation message
            Assert.Contains("Execution was cancelled.", assistantMsg.RawStream);
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
    public async Task CancelAsync_WhenCancelledImmediatelyWithNoEvents_AddsCancelledMessageWithoutCrashing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilCancelEmptyTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var cancellableSession = new CancellableTestSession();
            var agentRunner = new CancellableAgentRunner(TestAgentRunner.Create(), cancellableSession);
            var serializer = new JsonEventSerializer();
            var namingService = new ChatSessionNamingService(agentRunner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);
            var fakeJobService = new FakeChatJobService();

            var execService = new ChatExecutionService(
                configService,
                chatService,
                agentRunner,
                namingService,
                serializer,
                logger: null,
                serviceProvider: null,
                jobService: fakeJobService);

            var session = chatService.CreateSession("codex", "gpt-5.6-sol");

            // Start execution
            var sendTask = execService.SendMessageAsync(session.Id, "Analyze the project");

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while ((!execService.IsGenerating(session.Id) || !cancellableSession.HasObservers) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
            Assert.True(execService.IsGenerating(session.Id));

            // Cancel immediately before any events are emitted
            await execService.CancelAsync(session.Id);

            Assert.False(execService.IsGenerating(session.Id));

            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            var assistantMsg = updatedSession.Messages.FirstOrDefault(m => m.Role == "assistant");
            Assert.NotNull(assistantMsg);

            Assert.Equal("Execution was cancelled.", assistantMsg.Content);
            Assert.NotNull(assistantMsg.RawStream);
            Assert.Contains("Execution was cancelled.", assistantMsg.RawStream);
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

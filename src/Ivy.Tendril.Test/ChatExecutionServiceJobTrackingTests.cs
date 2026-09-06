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
    public void ToolResultEvent_WithJobIdPattern_TracksSpawnedJob()
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

            execService.TryTrackSpawnedJob(session.Id, "Created new background execution. Job ID: test-job-999");

            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.NotNull(updatedSession.SpawnedJobIds);
            Assert.Contains("test-job-999", updatedSession.SpawnedJobIds);
            Assert.Equal(session.Id, fakeJobService.JobChatSessions["test-job-999"]);
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
}

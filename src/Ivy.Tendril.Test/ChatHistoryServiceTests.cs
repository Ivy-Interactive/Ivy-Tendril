using System;
using System.IO;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test;

public class ChatHistoryServiceTests
{
    private (ChatHistoryService Service, string TempDir) CreateTestService()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var configService = new ConfigService(new TendrilSettings(), tempDir);
        var service = new ChatHistoryService(configService);
        return (service, tempDir);
    }

    [Fact]
    public void CreateSession_CreatesNewSessionWithAgentAndModel()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "opus", "Test Session");
            Assert.NotNull(session);
            Assert.Equal("claude", session.AgentId);
            Assert.Equal("opus", session.ModelId);
            Assert.Equal("Test Session", session.Title);

            var retrieved = service.GetSession(session.Id);
            Assert.NotNull(retrieved);
            Assert.Equal(session.Id, retrieved.Id);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AddMessage_UpdatesSessionAndPersistsMessage()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("opencode", "kimi-k3");
            var userMsg = service.AddMessage(session.Id, "user", "Hello agent", "opencode", "kimi-k3");

            Assert.NotNull(userMsg);
            Assert.Equal("user", userMsg.Role);
            Assert.Equal("Hello agent", userMsg.Content);

            var updatedSession = service.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Single(updatedSession.Messages);
            Assert.Equal("New Chat", updatedSession.Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DeleteSession_RemovesSessionFromStorage()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "sonnet");
            Assert.NotNull(service.GetSession(session.Id));

            service.DeleteSession(session.Id);
            Assert.Null(service.GetSession(session.Id));
            Assert.Empty(service.GetSessions());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RenameSession_UpdatesTitleAndFiresEvent()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "sonnet", "Old Title");
            var eventFired = false;
            service.SessionsChanged += (sender, args) => eventFired = true;

            service.RenameSession(session.Id, "New Title");

            var updatedSession = service.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Equal("New Title", updatedSession.Title);
            Assert.True(eventFired);

            // Verify persistence by reloading service from same temp directory
            var newConfigService = new ConfigService(new TendrilSettings(), tempDir);
            var reloadedService = new ChatHistoryService(newConfigService);
            var reloadedSession = reloadedService.GetSession(session.Id);
            Assert.NotNull(reloadedSession);
            Assert.Equal("New Title", reloadedSession.Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SetSessionGenerating_TracksGeneratingAndCompletedState()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "sonnet", "Test");
            var eventCount = 0;
            service.GeneratingSessionsChanged += (sender, args) => eventCount++;

            // Start generating
            service.SetSessionGenerating(session.Id, true);
            Assert.Contains(session.Id, service.GetGeneratingSessionIds());
            Assert.DoesNotContain(session.Id, service.GetCompletedSessionIds());
            Assert.Equal(1, eventCount);

            // Finish generating -> becomes completed
            service.SetSessionGenerating(session.Id, false);
            Assert.DoesNotContain(session.Id, service.GetGeneratingSessionIds());
            Assert.Contains(session.Id, service.GetCompletedSessionIds());
            Assert.Equal(2, eventCount);

            // Clear completed
            service.ClearSessionCompleted(session.Id);
            Assert.DoesNotContain(session.Id, service.GetCompletedSessionIds());
            Assert.Equal(3, eventCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AddMessage_WithAttachmentsAndEmptyPrompt_PersistsMessageCleanly()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "opus");

            // Format message when prompt is empty but attachments exist
            var attachedFiles = new[] { "/path/to/image1.png", "/path/to/doc.pdf" };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Attached Files]:");
            foreach (var file in attachedFiles)
            {
                sb.AppendLine($"- {file}");
            }
            var promptWithAttachments = sb.ToString().TrimEnd();

            var userMsg = service.AddMessage(session.Id, "user", promptWithAttachments, "claude", "opus");
            Assert.NotNull(userMsg);
            Assert.StartsWith("[Attached Files]:", userMsg.Content);
            Assert.Contains("image1.png", userMsg.Content);
            Assert.Contains("doc.pdf", userMsg.Content);

            var updatedSession = service.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Single(updatedSession.Messages);
            Assert.Equal(promptWithAttachments, updatedSession.Messages[0].Content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AddMessage_WithAttachmentErrors_RecordsAssistantWarningMessage()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "opus");
            var warningMessage = "Warning: Some attachments could not be processed:\n- Failed to process attachment 'corrupt.bin': Invalid data";

            var assistantMsg = service.AddMessage(session.Id, "assistant", warningMessage, "claude", "opus");
            Assert.NotNull(assistantMsg);
            Assert.Equal("assistant", assistantMsg.Role);
            Assert.Contains("Warning: Some attachments could not be processed", assistantMsg.Content);

            var updatedSession = service.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Single(updatedSession.Messages);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void QueuedMessages_Enqueue_Get_And_Dequeue_FIFO()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "opus");
            var eventFiredCount = 0;
            service.GeneratingSessionsChanged += (s, e) => eventFiredCount++;

            var dto1 = new Ivy.Tendril.Widgets.ChatSendMessageDto("First message", null, session.Id);
            var dto2 = new Ivy.Tendril.Widgets.ChatSendMessageDto("Second message", null, session.Id);

            var item1 = service.EnqueueMessage(session.Id, dto1);
            var item2 = service.EnqueueMessage(session.Id, dto2);

            Assert.NotNull(item1);
            Assert.NotNull(item2);
            Assert.Equal(2, eventFiredCount);

            var queued = service.GetQueuedMessages(session.Id);
            Assert.Equal(2, queued.Count);
            Assert.Equal("First message", queued[0].Prompt);
            Assert.Equal("Second message", queued[1].Prompt);

            var dequeued1 = service.TryDequeueMessage(session.Id, out var next1);
            Assert.True(dequeued1);
            Assert.NotNull(next1);
            Assert.Equal(item1.Id, next1.Id);
            Assert.Equal("First message", next1.Prompt);

            var queuedAfterFirst = service.GetQueuedMessages(session.Id);
            Assert.Single(queuedAfterFirst);
            Assert.Equal("Second message", queuedAfterFirst[0].Prompt);

            var dequeued2 = service.TryDequeueMessage(session.Id, out var next2);
            Assert.True(dequeued2);
            Assert.NotNull(next2);
            Assert.Equal(item2.Id, next2.Id);

            var dequeuedEmpty = service.TryDequeueMessage(session.Id, out var nextEmpty);
            Assert.False(dequeuedEmpty);
            Assert.Null(nextEmpty);
            Assert.Empty(service.GetQueuedMessages(session.Id));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void QueuedMessages_Remove_Update_And_Clear()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "opus");
            var dto1 = new Ivy.Tendril.Widgets.ChatSendMessageDto("Item 1", null, session.Id);
            var dto2 = new Ivy.Tendril.Widgets.ChatSendMessageDto("Item 2", null, session.Id);
            var dto3 = new Ivy.Tendril.Widgets.ChatSendMessageDto("Item 3", null, session.Id);

            var item1 = service.EnqueueMessage(session.Id, dto1);
            var item2 = service.EnqueueMessage(session.Id, dto2);
            var item3 = service.EnqueueMessage(session.Id, dto3);

            // Update item2
            var updated = service.UpdateQueuedMessage(session.Id, item2.Id, "Item 2 Updated");
            Assert.True(updated);

            var queued = service.GetQueuedMessages(session.Id);
            Assert.Equal(3, queued.Count);
            Assert.Equal("Item 2 Updated", queued[1].Prompt);

            // Remove item1
            var removed = service.RemoveQueuedMessage(session.Id, item1.Id);
            Assert.True(removed);

            var queuedAfterRemove = service.GetQueuedMessages(session.Id);
            Assert.Equal(2, queuedAfterRemove.Count);
            Assert.Equal("Item 2 Updated", queuedAfterRemove[0].Prompt);
            Assert.Equal("Item 3", queuedAfterRemove[1].Prompt);

            // Clear session queued messages
            service.ClearQueuedMessages(session.Id);
            Assert.Empty(service.GetQueuedMessages(session.Id));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DeleteSession_CleansUpQueuedMessages()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "opus");
            service.EnqueueMessage(session.Id, new Ivy.Tendril.Widgets.ChatSendMessageDto("Queued item", null, session.Id));
            Assert.Single(service.GetQueuedMessages(session.Id));

            service.DeleteSession(session.Id);
            Assert.Empty(service.GetQueuedMessages(session.Id));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ChatAttachmentDto_SupportsFileIdAndLocalPath()
    {
        var att = new Ivy.Tendril.Widgets.ChatAttachmentDto(
            Name: "report.pdf",
            ContentType: "application/pdf",
            Size: 1024,
            LocalPath: "/tmp/attachments/report.pdf",
            FileId: "att-12345"
        );

        Assert.Equal("report.pdf", att.Name);
        Assert.Equal("application/pdf", att.ContentType);
        Assert.Equal(1024, att.Size);
        Assert.Null(att.Base64Data);
        Assert.Equal("/tmp/attachments/report.pdf", att.LocalPath);
        Assert.Equal("att-12345", att.FileId);
    }

    [Fact]
    public void AddSpawnedJob_And_GetSpawnedJobs_TracksAndPersistsJobIds()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "opus");
            Assert.Empty(service.GetSpawnedJobs(session.Id));

            service.AddSpawnedJob(session.Id, "job-101");
            service.AddSpawnedJob(session.Id, "job-102");
            // Should not duplicate
            service.AddSpawnedJob(session.Id, "job-101");

            var jobs = service.GetSpawnedJobs(session.Id);
            Assert.Equal(2, jobs.Count);
            Assert.Equal("job-101", jobs[0]);
            Assert.Equal("job-102", jobs[1]);

            var storedSession = service.GetSession(session.Id);
            Assert.NotNull(storedSession);
            Assert.NotNull(storedSession.SpawnedJobIds);
            Assert.Equal(new[] { "job-101", "job-102" }, storedSession.SpawnedJobIds);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ApplyQuestionAnswers_UpdatesMessageContentWithAnswers()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "opus");
            var initialContent = "Here are questions:\n\n```questions\n- id: target_env\n  title: Which env?\n  options:\n    - title: Staging\n      value: staging\n    - title: Production\n      value: prod\n```\n";
            var msg = service.AddMessage(session.Id, "assistant", initialContent);

            var answers = new Dictionary<string, string[]>
            {
                ["target_env"] = ["staging"]
            };

            var success = service.ApplyQuestionAnswers(session.Id, msg.Id, answers);
            Assert.True(success);

            var updatedSession = service.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            var updatedMsg = Assert.Single(updatedSession.Messages);
            Assert.Contains("answer: staging", updatedMsg.Content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ApplyQuestionAnswers_UpdatesRawStreamWhenPresent()
    {
        var (service, tempDir) = CreateTestService();
        try
        {
            var session = service.CreateSession("claude", "opus");
            var initialContent = "Which env?\n\n```questions\n- id: target_env\n  title: Which env?\n  options:\n    - title: Staging\n      value: staging\n```\n";
            var rawStreamLine = "{\"kind\":\"text\",\"text\":\"Which env?\\n\\n```questions\\n- id: target_env\\n  title: Which env?\\n  options:\\n    - title: Staging\\n      value: staging\\n```\\n\",\"delta\":false}";
            var msg = service.AddMessage(session.Id, "assistant", initialContent, rawStream: rawStreamLine);

            var answers = new Dictionary<string, string[]>
            {
                ["target_env"] = ["staging"]
            };

            var success = service.ApplyQuestionAnswers(session.Id, msg.Id, answers);
            Assert.True(success);

            var updatedSession = service.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            var updatedMsg = Assert.Single(updatedSession.Messages);
            Assert.Contains("answer: staging", updatedMsg.Content);
            Assert.NotNull(updatedMsg.RawStream);
            Assert.Contains("answer: staging", updatedMsg.RawStream);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}


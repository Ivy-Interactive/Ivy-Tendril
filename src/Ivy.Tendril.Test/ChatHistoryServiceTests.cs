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
}

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
            Assert.Equal("Hello agent", updatedSession.Title);
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
}

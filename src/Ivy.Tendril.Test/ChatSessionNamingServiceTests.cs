using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test;

public class ChatSessionNamingServiceTests
{
    private class MockAgentRunner : IAgentRunner
    {
        public Func<AgentResolutionContext, CancellationToken, Task<ResultEvent>>? RunToCompletionHandler { get; set; }
        public AgentResolutionContext? LastContext { get; private set; }

        public IReadOnlyList<string> RegisteredAgents => ["claude", "opencode"];
        public IReadOnlyList<IAgentSession> ActiveSessions => [];
        public IObservable<IAgentSession> Sessions => throw new NotImplementedException();

        public IAgentCli GetCli(string agentId) => throw new NotImplementedException();
        public IEventParser GetParser(string agentId) => throw new NotImplementedException();
        public IAgentHealthCheck GetHealthCheck(string agentId) => throw new NotImplementedException();
        public IAgentDescriptor GetDescriptor(string agentId) => throw new NotImplementedException();
        public IFailureAnalyzer? GetFailureAnalyzer(string agentId) => null;
        public ISessionCostParser? GetCostParser(string agentId) => null;
        public IAgentPty? GetPty(string agentId) => null;
        public IModelCatalogProvider? GetModelCatalog(string agentId) => null;
        public IEnumerable<IModelCatalogProvider> ModelCatalogs => [];

        public Task<IAgentSession> LaunchAsync(AgentResolutionContext context, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ResultEvent> RunToCompletionAsync(AgentResolutionContext context, CancellationToken ct = default)
        {
            LastContext = context;
            if (RunToCompletionHandler != null)
            {
                return RunToCompletionHandler(context, ct);
            }

            return Task.FromResult(new ResultEvent
            {
                Kind = AgentEventKind.Result,
                IsSuccess = true,
                Response = "Fixing User Authentication"
            });
        }

        public Task StopAllAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private (ChatHistoryService ChatService, ConfigService ConfigService, string TempDir) CreateServices()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilNamingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var config = new ConfigService(new TendrilSettings(), tempDir);
        var chat = new ChatHistoryService(config);
        return (chat, config, tempDir);
    }

    [Fact]
    public void BuildPrompt_FormatsPromptWithUserAndAssistantMessages()
    {
        var userPrompt = "How do I fix the database connection?";
        var assistantResponse = "You need to update the connection string in config.yaml.";

        var prompt = ChatSessionNamingService.BuildPrompt(userPrompt, assistantResponse);

        Assert.Contains("How do I fix the database connection?", prompt);
        Assert.Contains("You need to update the connection string in config.yaml.", prompt);
        Assert.Contains("short 3 to 6 word title", prompt);
    }

    [Theory]
    [InlineData("### User Authentication Flow", "User Authentication Flow")]
    [InlineData("# **Fixing Database Deadlocks**", "Fixing Database Deadlocks")]
    [InlineData("`Refactoring Service Layer`", "Refactoring Service Layer")]
    [InlineData("*Updating Unit Tests*", "Updating Unit Tests")]
    public void CleanGeneratedTitle_StripsMarkdownHeadingsAndFormatting(string raw, string expected)
    {
        var cleaned = ChatSessionNamingService.CleanGeneratedTitle(raw);
        Assert.Equal(expected, cleaned);
    }

    [Theory]
    [InlineData("Title: Debugging Login API", "Debugging Login API")]
    [InlineData("title: \"Optimizing Memory Usage\"", "Optimizing Memory Usage")]
    [InlineData("Topic: 'Configuring Docker Container'", "Configuring Docker Container")]
    [InlineData("Subject: Resolving Merge Conflicts", "Resolving Merge Conflicts")]
    [InlineData("“Refactoring Navigation Bar”", "Refactoring Navigation Bar")]
    public void CleanGeneratedTitle_StripsPrefixesAndQuotes(string raw, string expected)
    {
        var cleaned = ChatSessionNamingService.CleanGeneratedTitle(raw);
        Assert.Equal(expected, cleaned);
    }

    [Theory]
    [InlineData("Fixing Build Errors...", "Fixing Build Errors")]
    [InlineData("Adding Dark Mode Support…", "Adding Dark Mode Support")]
    [InlineData("Updating API Routes.", "Updating API Routes")]
    [InlineData("Investigating High CPU Usage!", "Investigating High CPU Usage")]
    [InlineData("Why is the test failing?", "Why is the test failing")]
    public void CleanGeneratedTitle_StripsTrailingPunctuationAndEllipses(string raw, string expected)
    {
        var cleaned = ChatSessionNamingService.CleanGeneratedTitle(raw);
        Assert.Equal(expected, cleaned);
    }

    [Fact]
    public void CleanGeneratedTitle_EnforcesMaxLength()
    {
        var veryLongTitle = "This is an extremely long title that exceeds the maximum allowable length of fifty characters";
        var cleaned = ChatSessionNamingService.CleanGeneratedTitle(veryLongTitle);

        Assert.NotNull(cleaned);
        Assert.True(cleaned.Length <= 50);
        Assert.Equal(veryLongTitle[..50].Trim(), cleaned);
    }

    [Fact]
    public async Task GenerateAndSetTitleAsync_RenamesSession_WhenTitleIsNewChat()
    {
        var (chatService, configService, tempDir) = CreateServices();
        try
        {
            var runner = new MockAgentRunner
            {
                RunToCompletionHandler = (_, _) => Task.FromResult(new ResultEvent
                {
                    Kind = AgentEventKind.Result,
                    IsSuccess = true,
                    Response = "Title: \"Configuring Redis Cache\"."
                })
            };

            var namingService = new ChatSessionNamingService(
                runner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);

            var session = chatService.CreateSession("claude", "sonnet");
            chatService.AddMessage(session.Id, "user", "How do I configure Redis cache?");
            chatService.AddMessage(session.Id, "assistant", "You can configure Redis in settings.");

            await namingService.GenerateAndSetTitleAsync(
                session.Id,
                "How do I configure Redis cache?",
                "You can configure Redis in settings.",
                "claude",
                "sonnet");

            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Equal("Configuring Redis Cache", updatedSession.Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GenerateAndSetTitleAsync_DoesNotRename_WhenTitleAlreadyCustomized()
    {
        var (chatService, configService, tempDir) = CreateServices();
        try
        {
            var runner = new MockAgentRunner
            {
                RunToCompletionHandler = (_, _) => Task.FromResult(new ResultEvent
                {
                    Kind = AgentEventKind.Result,
                    IsSuccess = true,
                    Response = "Generated Redis Cache Title"
                })
            };

            var namingService = new ChatSessionNamingService(
                runner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);

            var session = chatService.CreateSession("claude", "sonnet", "Customized User Title");
            chatService.AddMessage(session.Id, "user", "How do I configure Redis cache?");
            chatService.AddMessage(session.Id, "assistant", "You can configure Redis in settings.");

            await namingService.GenerateAndSetTitleAsync(
                session.Id,
                "How do I configure Redis cache?",
                "You can configure Redis in settings.",
                "claude",
                "sonnet");

            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Equal("Customized User Title", updatedSession.Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GenerateAndSetTitleAsync_HandlesRunnerFailureGracefully()
    {
        var (chatService, configService, tempDir) = CreateServices();
        try
        {
            var runner = new MockAgentRunner
            {
                RunToCompletionHandler = (_, _) => Task.FromResult(new ResultEvent
                {
                    Kind = AgentEventKind.Result,
                    IsSuccess = false,
                    Error = "Model rate limit exceeded",
                    ExitCode = 1
                })
            };

            var namingService = new ChatSessionNamingService(
                runner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);

            var session = chatService.CreateSession("claude", "sonnet");
            chatService.AddMessage(session.Id, "user", "Prompt");
            chatService.AddMessage(session.Id, "assistant", "Response");

            await namingService.GenerateAndSetTitleAsync(
                session.Id,
                "Prompt",
                "Response",
                "claude",
                "sonnet");

            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Equal("New Chat", updatedSession.Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GenerateAndSetTitleAsync_HandlesRunnerTimeoutGracefully()
    {
        var (chatService, configService, tempDir) = CreateServices();
        try
        {
            var runner = new MockAgentRunner
            {
                RunToCompletionHandler = async (_, ct) =>
                {
                    await Task.Delay(50, ct);
                    throw new OperationCanceledException();
                }
            };

            var namingService = new ChatSessionNamingService(
                runner, configService, chatService, NullLogger<ChatSessionNamingService>.Instance);

            var session = chatService.CreateSession("claude", "sonnet");
            chatService.AddMessage(session.Id, "user", "Prompt");
            chatService.AddMessage(session.Id, "assistant", "Response");

            await namingService.GenerateAndSetTitleAsync(
                session.Id,
                "Prompt",
                "Response",
                "claude",
                "sonnet");

            var updatedSession = chatService.GetSession(session.Id);
            Assert.NotNull(updatedSession);
            Assert.Equal("New Chat", updatedSession.Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}

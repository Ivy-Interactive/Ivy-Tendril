using System;
using System.IO;
using System.Linq;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class ChatLauncherTests
{
    private static ConfigService Config(string chatMode, string? tempDir = null) =>
        new(new TendrilSettings { CodingAgent = "codex", ChatMode = chatMode }, tempDir);

    [Fact]
    public void TargetFor_ChatMode_OpensChatAppWithPrompt()
    {
        var (app, args) = ChatLauncher.TargetFor(Config(ChatModes.Chat), "Discuss plan", "#7");

        Assert.Equal(typeof(ChatApp), app);
        var chatArgs = Assert.IsType<ChatAppArgs>(args);
        Assert.Equal("Discuss plan", chatArgs.Prompt);
        Assert.Null(chatArgs.SessionId);
    }

    [Fact]
    public void TargetFor_TerminalMode_OpensAgentAppWithPromptAndTitle()
    {
        var (app, args) = ChatLauncher.TargetFor(Config(ChatModes.Terminal), "Discuss plan", "#7");

        Assert.Equal(typeof(AgentApp), app);
        var agentArgs = Assert.IsType<AgentAppArgs>(args);
        Assert.Equal(("Discuss plan", "#7", null), (agentArgs.Prompt, agentArgs.Title, agentArgs.SessionId));
    }

    [Fact]
    public void NewSessionTarget_ChatMode_CreatesChatSessionAndSelectsIt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilLauncherTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = Config(ChatModes.Chat, tempDir);
            var chats = new ChatHistoryService(config);

            var (app, args) = ChatLauncher.NewSessionTarget(config, chats, TestAgentRunner.Create());

            Assert.Equal(typeof(ChatApp), app);
            var chatArgs = Assert.IsType<ChatAppArgs>(args);
            var session = Assert.Single(chats.GetSessions());
            Assert.Equal(session.Id, chatArgs.SessionId);
            Assert.False(session.IsTerminal());
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void NewSessionTarget_TerminalMode_DefersSessionCreationToTheShell()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilLauncherTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = Config(ChatModes.Terminal, tempDir);
            var chats = new ChatHistoryService(config);

            var (app, args) = ChatLauncher.NewSessionTarget(config, chats, TestAgentRunner.Create());

            Assert.Equal(typeof(AgentApp), app);
            var agentArgs = Assert.IsType<AgentAppArgs>(args);
            Assert.Null(agentArgs.SessionId);
            Assert.Empty(chats.GetSessions());
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}

using System;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test;

public class ChatModesTests
{
    [Theory]
    [InlineData(null, ChatModes.Chat)]
    [InlineData("", ChatModes.Chat)]
    [InlineData("chat", ChatModes.Chat)]
    [InlineData("terminal", ChatModes.Terminal)]
    [InlineData("Terminal", ChatModes.Terminal)]
    [InlineData("pty", ChatModes.Chat)]
    public void Normalize_FallsBackToChatForAnythingButTerminal(string? mode, string expected)
    {
        Assert.Equal(expected, ChatModes.Normalize(mode));
    }

    [Fact]
    public void TendrilSettings_DefaultsToChatMode()
    {
        Assert.Equal(ChatModes.Chat, new TendrilSettings().ChatMode);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("chat", false)]
    [InlineData("terminal", true)]
    [InlineData("TERMINAL", true)]
    public void ChatSessionKinds_IsTerminal_ReadsTheSessionKind(string? kind, bool expected)
    {
        var session = new ChatSessionModel("id", "t", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "claude", "opus", [], Kind: kind);

        Assert.Equal(expected, session.IsTerminal());
    }
}

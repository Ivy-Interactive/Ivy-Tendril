using Ivy.Tendril.Apps.Chat;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class ChatAppArgsTests
{
    [Fact]
    public void NewChat_NamesNoSessionAndNoPrompt()
    {
        var args = new ChatAppArgs(NewChat: true);

        Assert.True(args.NewChat);
        Assert.Null(args.SessionId);
        Assert.Null(args.Prompt);
    }

    [Fact]
    public void NewChat_IsNotEqualToTheDefaultArgs()
    {
        // The shell keys a page on its args, so this inequality is what makes clicking + rebuild the
        // chat view rather than leaving the previous session on screen.
        Assert.NotEqual(new ChatAppArgs(), new ChatAppArgs(NewChat: true));
        Assert.NotEqual(new ChatAppArgs(SessionId: "abc"), new ChatAppArgs(NewChat: true));
        Assert.Equal(new ChatAppArgs(NewChat: true), new ChatAppArgs(NewChat: true));
    }

    [Fact]
    public void DefaultArgs_DoNotRequestANewChat()
    {
        Assert.False(new ChatAppArgs().NewChat);
        Assert.False(new ChatAppArgs(SessionId: "abc").NewChat);
        Assert.False(new ChatAppArgs(Prompt: "Hello").NewChat);
    }
}

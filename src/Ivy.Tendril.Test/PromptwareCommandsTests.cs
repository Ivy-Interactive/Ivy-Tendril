using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class PromptwareCommandsTests
{
    [Fact]
    public void Handle_ReturnsNegativeOne_ForEmptyArgs()
    {
        var result = PromptwareCommands.Handle(Array.Empty<string>());
        Assert.Equal(-1, result);
    }

    [Fact]
    public void Handle_ReturnsNegativeOne_ForUnknownCommand()
    {
        var result = PromptwareCommands.Handle(new[] { "unknown-command" });
        Assert.Equal(-1, result);
    }

    [Fact]
    public void Handle_MatchesUpdatePromptwaresCommand()
    {
        var prev = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var originalOut = Console.Out;
        try
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", null);
            Console.SetOut(new StringWriter());
            var result = PromptwareCommands.Handle(new[] { "update-promptwares" });
            Assert.NotEqual(-1, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", prev);
            Console.SetOut(originalOut);
        }
    }
}
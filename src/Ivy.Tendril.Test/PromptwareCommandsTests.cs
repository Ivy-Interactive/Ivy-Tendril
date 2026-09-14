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
        // An empty home rather than an unset TENDRIL_HOME: this dispatches into the real
        // update-promptwares handler, and with no home set it would resolve (and write to) the
        // machine's live Tendril home. See TendrilHomeIsolation.
        var emptyHome = Path.Combine(TendrilHomeIsolation.Root, $"promptware-dispatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyHome);
        var originalOut = Console.Out;
        try
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", emptyHome);
            Console.SetOut(new StringWriter());
            var result = PromptwareCommands.Handle(new[] { "update-promptwares" });
            Assert.NotEqual(-1, result);
        }
        finally
        {
            TendrilHomeIsolation.Apply();
            Console.SetOut(originalOut);
        }
    }
}

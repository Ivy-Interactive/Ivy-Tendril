using Ivy.Tendril.Apps.ReviewAction;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.TestHelpers;

namespace Ivy.Tendril.Test.Apps;

public class ReviewActionAppTests
{
    private static StubConfigService BuildConfig() => new([
        new ProjectConfig
        {
            Name = "Tendril",
            ReviewActions =
            [
                new ReviewActionConfig { Name = "Run App", Condition = "$true", Command = "dotnet run" },
                new ReviewActionConfig { Name = "No Command", Condition = "$true", Command = "" }
            ]
        }
    ]);

    [Fact]
    public void ResolveAction_KnownActionWithCommand_ReturnsAction()
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), "Tendril", "Run App");

        Assert.NotNull(action);
        Assert.Equal("dotnet run", action.Command);
    }

    [Fact]
    public void ResolveAction_UnknownActionName_ReturnsNull()
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), "Tendril", "Does Not Exist");

        Assert.Null(action);
    }

    [Fact]
    public void ResolveAction_ActionWithEmptyCommand_ReturnsNull()
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), "Tendril", "No Command");

        Assert.Null(action);
    }

    [Fact]
    public void ResolveAction_UnknownProject_ReturnsNull()
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), "NoSuchProject", "Run App");

        Assert.Null(action);
    }

    [Theory]
    [InlineData(null, "Run App")]
    [InlineData("Tendril", null)]
    public void ResolveAction_MissingProjectOrActionName_ReturnsNull(string? project, string? actionName)
    {
        var action = ReviewActionApp.ResolveAction(BuildConfig(), project, actionName);

        Assert.Null(action);
    }
}

using Ivy.Tendril.Apps.Jobs.Dialogs;
using Ivy.Tendril.Models;
using Xunit;

namespace Ivy.Tendril.Test;

public class RerunJobDialogTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BuildRerunArgs_NoFeedback_ReturnsOriginalArgs(string? feedback)
    {
        var original = new ExecutePlanArgs("/plans/00001-Test");

        var result = RerunJobDialog.BuildRerunArgs(original, feedback);

        Assert.Same(original, result);
    }

    [Fact]
    public void BuildRerunArgs_ExecutePlanWithFeedback_BecomesRetryWithChangeRequest()
    {
        var original = new ExecutePlanArgs("/plans/00001-Test");

        var result = RerunJobDialog.BuildRerunArgs(original, "fix the readme");

        var retry = Assert.IsType<RetryPlanArgs>(result);
        Assert.Equal("/plans/00001-Test", retry.FolderPath);
        Assert.Equal("fix the readme", retry.ChangeRequest);
    }

    [Fact]
    public void BuildRerunArgs_RetryPlanWithFeedback_KeepsRetryWithNewChangeRequest()
    {
        var original = new RetryPlanArgs("/plans/00001-Test", "old request");

        var result = RerunJobDialog.BuildRerunArgs(original, "new request");

        var retry = Assert.IsType<RetryPlanArgs>(result);
        Assert.Equal("/plans/00001-Test", retry.FolderPath);
        Assert.Equal("new request", retry.ChangeRequest);
    }

    [Fact]
    public void BuildRerunArgs_UpdatePlanWithFeedback_BecomesUpdateWithInstructions()
    {
        var original = new UpdatePlanArgs("/plans/00001-Test", "old instructions");

        var result = RerunJobDialog.BuildRerunArgs(original, "new instructions");

        var update = Assert.IsType<UpdatePlanArgs>(result);
        Assert.Equal("/plans/00001-Test", update.FolderPath);
        Assert.Equal("new instructions", update.Instructions);
    }

    [Fact]
    public void BuildRerunArgs_UnsupportedTypeWithFeedback_ReturnsOriginalArgs()
    {
        var original = new CreatePrArgs("/plans/00001-Test");

        var result = RerunJobDialog.BuildRerunArgs(original, "some feedback");

        Assert.Same(original, result);
    }

    [Theory]
    [InlineData(typeof(ExecutePlanArgs), true)]
    [InlineData(typeof(RetryPlanArgs), true)]
    [InlineData(typeof(UpdatePlanArgs), true)]
    [InlineData(typeof(ExpandPlanArgs), false)]
    [InlineData(typeof(CreatePrArgs), false)]
    public void SupportsFeedback_MatchesJobType(Type argsType, bool expected)
    {
        JobArgsBase args = argsType switch
        {
            _ when argsType == typeof(ExecutePlanArgs) => new ExecutePlanArgs("/plans/00001-Test"),
            _ when argsType == typeof(RetryPlanArgs) => new RetryPlanArgs("/plans/00001-Test", "req"),
            _ when argsType == typeof(UpdatePlanArgs) => new UpdatePlanArgs("/plans/00001-Test"),
            _ when argsType == typeof(ExpandPlanArgs) => new ExpandPlanArgs("/plans/00001-Test"),
            _ => new CreatePrArgs("/plans/00001-Test")
        };

        Assert.Equal(expected, RerunJobDialog.SupportsFeedback(args));
    }
}

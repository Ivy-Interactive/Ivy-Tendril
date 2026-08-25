using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Apps;

public class ReviewActionsBarViewTests
{
    [Fact]
    public void GetTooltip_ConditionNotMet_WithCondition_ReturnsDisabledReason()
    {
        var action = new ReviewActionConfig
        {
            Name = "Run Tests",
            Condition = "Test-Path ./build/passed.txt",
            Command = "dotnet test"
        };

        var tooltip = ReviewActionsBarView.GetTooltip(action, conditionMet: false);

        Assert.Equal("Disabled: Condition not met (Test-Path ./build/passed.txt)", tooltip);
    }

    [Fact]
    public void GetTooltip_ConditionNotMet_WithoutCondition_ReturnsGenericDisabledReason()
    {
        var action = new ReviewActionConfig
        {
            Name = "Run Tests",
            Condition = "",
            Command = "dotnet test"
        };

        var tooltip = ReviewActionsBarView.GetTooltip(action, conditionMet: false);

        Assert.Equal("Disabled: Condition not met", tooltip);
    }

    [Fact]
    public void GetTooltip_ConditionMet_WithCommand_ReturnsRunCommand()
    {
        var action = new ReviewActionConfig
        {
            Name = "Run Tests",
            Condition = "Test-Path ./build",
            Command = "dotnet test"
        };

        var tooltip = ReviewActionsBarView.GetTooltip(action, conditionMet: true);

        Assert.Equal("Run: dotnet test", tooltip);
    }

    [Fact]
    public void BuildActionButton_Disabled_CreatesDisabledButtonWithTooltip()
    {
        var action = new ReviewActionConfig
        {
            Name = "Verify",
            Condition = "Test-Path ./Verification/summary.md",
            Command = "dotnet run verify"
        };

        var button = ReviewActionsBarView.BuildActionButton(action, conditionMet: false);

        Assert.NotNull(button);
        Assert.True(button.Disabled);
        Assert.Equal("Disabled: Condition not met (Test-Path ./Verification/summary.md)", button.Tooltip);
    }

    [Fact]
    public void BuildActionButton_Enabled_CreatesEnabledButtonWithTooltip()
    {
        var action = new ReviewActionConfig
        {
            Name = "Verify",
            Condition = "Test-Path ./Verification/summary.md",
            Command = "dotnet run verify"
        };

        var button = ReviewActionsBarView.BuildActionButton(action, conditionMet: true);

        Assert.NotNull(button);
        Assert.False(button.Disabled);
        Assert.Equal("Run: dotnet run verify", button.Tooltip);
    }
}

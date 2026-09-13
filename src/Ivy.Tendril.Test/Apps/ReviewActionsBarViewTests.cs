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

    // --- Allocated ports in the tooltip ---

    [Fact]
    public void GetTooltip_WithAllocatedPorts_AppendsThemToTheCommand()
    {
        var action = new ReviewActionConfig { Name = "Run", Condition = "$true", Command = "dotnet run" };

        var tooltip = ReviewActionsBarView.GetTooltip(action, conditionMet: true,
            new Dictionary<string, int> { ["frontend"] = 3000, ["backend"] = 3001 });

        // Sorted by name so the tooltip does not reshuffle between renders.
        Assert.Equal("Run: dotnet run (ports: backend: 3001, frontend: 3000)", tooltip);
    }

    [Fact]
    public void GetTooltip_WithAllocatedPortsAndNoCommand_AppendsThemToTheActionName()
    {
        var action = new ReviewActionConfig { Name = "Run", Condition = "$true", Command = "" };

        var tooltip = ReviewActionsBarView.GetTooltip(action, conditionMet: true,
            new Dictionary<string, int> { ["backend"] = 3001 });

        Assert.Equal("Run Run (ports: backend: 3001)", tooltip);
    }

    [Fact]
    public void GetTooltip_EmptyAllocatedPorts_OmitsThePortSuffix()
    {
        var action = new ReviewActionConfig { Name = "Run", Condition = "$true", Command = "dotnet run" };

        var tooltip = ReviewActionsBarView.GetTooltip(action, conditionMet: true, new Dictionary<string, int>());

        Assert.Equal("Run: dotnet run", tooltip);
    }

    [Fact]
    public void GetTooltip_ConditionNotMet_IgnoresAllocatedPorts()
    {
        var action = new ReviewActionConfig { Name = "Run", Condition = "$false", Command = "dotnet run" };

        var tooltip = ReviewActionsBarView.GetTooltip(action, conditionMet: false,
            new Dictionary<string, int> { ["backend"] = 3001 });

        Assert.Equal("Disabled: Condition not met ($false)", tooltip);
    }

    [Fact]
    public void BuildActionButton_WithAllocatedPorts_ShowsThemInTheTooltip()
    {
        var action = new ReviewActionConfig { Name = "Run", Condition = "$true", Command = "dotnet run" };

        var button = ReviewActionsBarView.BuildActionButton(action, conditionMet: true,
            allocatedPorts: new Dictionary<string, int> { ["backend"] = 3001 });

        Assert.Equal("Run: dotnet run (ports: backend: 3001)", button.Tooltip);
    }
}

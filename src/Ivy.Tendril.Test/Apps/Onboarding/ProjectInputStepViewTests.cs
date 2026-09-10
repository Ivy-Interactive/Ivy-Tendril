using Ivy.Tendril.Apps.Onboarding;

namespace Ivy.Tendril.Test.Apps.Onboarding;

public class ProjectInputStepViewTests
{
    [Fact]
    public void BackgroundButton_RendersWithBackgroundLabel()
    {
        var button = ProjectInputStepView.BuildBackgroundButton(canContinue: true, onClick: () => { });

        Assert.Equal("Background", button.Title);
        Assert.Equal(ProjectInputStepView.BackgroundButtonLabel, button.Title);
    }

    [Fact]
    public void BackgroundButton_WhenCanContinueIsTrue_IsNotDisabled()
    {
        var button = ProjectInputStepView.BuildBackgroundButton(canContinue: true, onClick: () => { });

        Assert.False(button.Disabled);
    }

    [Fact]
    public void BackgroundButton_WhenCanContinueIsFalse_IsDisabled()
    {
        var button = ProjectInputStepView.BuildBackgroundButton(canContinue: false, onClick: () => { });

        Assert.True(button.Disabled);
    }
}

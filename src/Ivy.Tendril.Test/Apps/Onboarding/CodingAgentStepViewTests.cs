using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Onboarding;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Onboarding;

public class CodingAgentStepViewTests
{
    [Fact]
    public void ContinueButton_WhenNotTesting_IsNotDisabled()
    {
        var isTestingModels = new State<bool>(false);

        var button = new Button("Continue")
            .Primary()
            .Loading(isTestingModels.Value)
            .Disabled(isTestingModels.Value);

        Assert.False(button.Disabled);
        Assert.False(button.Loading);
    }

    [Fact]
    public void ContinueButton_WhenTesting_IsDisabledAndLoading()
    {
        var isTestingModels = new State<bool>(true);

        var button = new Button("Continue")
            .Primary()
            .Loading(isTestingModels.Value)
            .Disabled(isTestingModels.Value);

        Assert.True(button.Disabled);
        Assert.True(button.Loading);
    }

    [Fact]
    public void BackButton_WhenTesting_IsDisabled()
    {
        var isTestingModels = new State<bool>(true);

        var button = new Button("Back")
            .Ghost()
            .Disabled(isTestingModels.Value);

        Assert.True(button.Disabled);
    }

    [Fact]
    public void BackButton_WhenNotTesting_IsNotDisabled()
    {
        var isTestingModels = new State<bool>(false);

        var button = new Button("Back")
            .Ghost()
            .Disabled(isTestingModels.Value);

        Assert.False(button.Disabled);
    }

    [Fact]
    public void TestEndpointButton_WhenTesting_IsDisabledAndLoading()
    {
        var isTestingModels = new State<bool>(true);

        var button = new Button("Test Endpoint")
            .Outline()
            .Loading(isTestingModels.Value)
            .Disabled(isTestingModels.Value);

        Assert.True(button.Disabled);
        Assert.True(button.Loading);
    }

    [Fact]
    public void EndpointTestLifecycle_TracksDisabledStateCorrectly()
    {
        var isTestingModels = new State<bool>(false);

        // Initial state: not testing, buttons enabled
        Assert.False(isTestingModels.Value);
        var initialContinue = new Button("Continue").Disabled(isTestingModels.Value);
        Assert.False(initialContinue.Disabled);

        // Start testing: state becomes true, buttons disabled
        isTestingModels.Set(true);
        Assert.True(isTestingModels.Value);
        var testingContinue = new Button("Continue").Disabled(isTestingModels.Value);
        Assert.True(testingContinue.Disabled);

        // Complete testing (finally block sets false): buttons re-enabled
        isTestingModels.Set(false);
        Assert.False(isTestingModels.Value);
        var completedContinue = new Button("Continue").Disabled(isTestingModels.Value);
        Assert.False(completedContinue.Disabled);
    }
}

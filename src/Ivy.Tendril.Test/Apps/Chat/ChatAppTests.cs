using Ivy.Tendril.Apps.Chat;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class ChatAppTests
{
    [Fact]
    public void GetModelsForAgent_ForClaude_ReturnsDefaultModelAsFirstOption()
    {
        var runner = TestAgentRunner.Create();
        var models = ChatApp.GetModelsForAgent(runner, "claude");

        Assert.NotEmpty(models);
        Assert.Equal("claude-opus-5", models[0].Id);
    }

    [Fact]
    public void ResolveModel_WithRunner_PicksDefaultModelWhenNoPreferenceSupplied()
    {
        var runner = TestAgentRunner.Create();
        var models = ChatApp.GetModelsForAgent(runner, "claude");

        var resolvedWithoutPreference = ChatApp.ResolveModel(runner, "claude", models, null);
        var resolvedWithUnknownPreference = ChatApp.ResolveModel(runner, "claude", models, "unknown-model-xyz");

        Assert.Equal("claude-opus-5", resolvedWithoutPreference);
        Assert.Equal("claude-opus-5", resolvedWithUnknownPreference);
    }

    [Fact]
    public void ResolveModel_PicksDefaultModelWhenNoPreferenceSupplied()
    {
        var runner = TestAgentRunner.Create();
        var models = ChatApp.GetModelsForAgent(runner, "claude");

        var resolved = ChatApp.ResolveModel(models, null);

        Assert.Equal("claude-opus-5", resolved);
    }
}

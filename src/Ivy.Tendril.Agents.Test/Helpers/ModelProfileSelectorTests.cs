using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Helpers;
using Xunit;

namespace Ivy.Tendril.Agents.Test.Helpers;

public sealed class ModelProfileSelectorTests
{
    [Fact]
    public void SelectDefaults_IvyProxy_PicksOpus5Deep_Gemini37Balanced_GeminiQuick()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "claude-opus-4", DisplayName = "Claude Opus 4" },
            new() { Id = "claude-opus-5", DisplayName = "Claude Opus 5" },
            new() { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5" },
            new() { Id = "gemini-2.5-flash-lite", DisplayName = "Gemini 2.5 Flash Lite" },
            new() { Id = "gemini-3.7-flash", DisplayName = "Gemini 3.7 Flash" },
            new() { Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash" },
        };

        var (deep, balanced, quick) = ModelProfileSelector.SelectDefaults(models, isIvy: true);

        Assert.Equal("claude-opus-5", deep);
        Assert.Equal("gemini-3.7-flash", balanced);
        Assert.Equal("gemini-3.7-flash", quick);
    }

    [Fact]
    public void SelectDefaults_Gemini36_WhenNo37_Picks36ForBalancedAndQuick()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "claude-opus-4", DisplayName = "Claude Opus 4" },
            new() { Id = "claude-opus-5", DisplayName = "Claude Opus 5" },
            new() { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5" },
            new() { Id = "gemini-2.5-flash-lite", DisplayName = "Gemini 2.5 Flash Lite" },
            new() { Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash" },
        };

        var (deep, balanced, quick) = ModelProfileSelector.SelectDefaults(models, isIvy: true);

        Assert.Equal("claude-opus-5", deep);
        Assert.Equal("gemini-3.6-flash", balanced);
        Assert.Equal("gemini-3.6-flash", quick);
    }

    [Fact]
    public void SelectDefaults_AnthropicOnly_PicksOpus5_Sonnet5_Haiku5()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "claude-3-5-haiku", DisplayName = "Claude 3.5 Haiku" },
            new() { Id = "claude-haiku-5", DisplayName = "Claude Haiku 5" },
            new() { Id = "claude-opus-4", DisplayName = "Claude Opus 4" },
            new() { Id = "claude-opus-5", DisplayName = "Claude Opus 5" },
            new() { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5" },
        };

        var (deep, balanced, quick) = ModelProfileSelector.SelectDefaults(models, isAnthropic: true);

        Assert.Equal("claude-opus-5", deep);
        Assert.Equal("claude-sonnet-5", balanced);
        Assert.Equal("claude-haiku-5", quick);
    }

    [Fact]
    public void SelectDefaults_OpenAiOnly_PicksSol_Terra_Luna()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o" },
            new() { Id = "gpt-4o-mini", DisplayName = "GPT-4o mini" },
            new() { Id = "gpt-5.6-sol", DisplayName = "GPT-5.6 Sol" },
            new() { Id = "gpt-5.6-terra", DisplayName = "GPT-5.6 Terra" },
            new() { Id = "gpt-5.6-luna", DisplayName = "GPT-5.6 Luna" },
        };

        var (deep, balanced, quick) = ModelProfileSelector.SelectDefaults(models, isOpenAi: true);

        Assert.Equal("gpt-5.6-sol", deep);
        Assert.Equal("gpt-5.6-terra", balanced);
        Assert.Equal("gpt-5.6-luna", quick);
    }

    [Fact]
    public void SelectDefaults_GeminiCatalog_PicksFlashDeep_FlashBalanced_FlashQuick()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "gemini-3.7-flash", DisplayName = "Gemini 3.7 Flash" },
            new() { Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash" },
            new() { Id = "gemini-3.1-pro", DisplayName = "Gemini 3.1 Pro" },
            new() { Id = "gemini-3-pro-preview", DisplayName = "Gemini 3 Pro" },
            new() { Id = "gemini-3-flash-preview", DisplayName = "Gemini 3 Flash" },
        };

        var (deep, balanced, quick) = ModelProfileSelector.SelectDefaults(models, isGoogle: true);

        Assert.Equal("gemini-3.7-flash", deep);
        Assert.Equal("gemini-3.7-flash", balanced);
        Assert.Equal("gemini-3.7-flash", quick);
    }

    [Fact]
    public void SelectDefaults_AntigravityCatalog_PicksFlashDeep_Gemini37Balanced_GeminiQuick()
    {
        var models = new List<ModelInfo>
        {
            new() { Id = "gemini-3.7-flash", DisplayName = "Gemini 3.7 Flash" },
            new() { Id = "gemini-3.6-flash", DisplayName = "Gemini 3.6 Flash" },
            new() { Id = "gemini-3.1-pro", DisplayName = "Gemini 3.1 Pro" },
            new() { Id = "claude-opus-5", DisplayName = "Claude Opus 5" },
            new() { Id = "claude-sonnet-5", DisplayName = "Claude Sonnet 5" },
            new() { Id = "gpt-oss-120b", DisplayName = "GPT-OSS 120B" },
        };

        var (deep, balanced, quick) = ModelProfileSelector.SelectDefaults(models, isGoogle: true);

        Assert.Equal("gemini-3.7-flash", deep);
        Assert.Equal("gemini-3.7-flash", balanced);
        Assert.Equal("gemini-3.7-flash", quick);
    }

    [Fact]
    public void SelectDefaults_EmptyModels_ReturnsCardDefaults()
    {
        var (deepIvy, balancedIvy, quickIvy) = ModelProfileSelector.SelectDefaults(null, isIvy: true);
        Assert.Equal("claude-opus-5", deepIvy);
        Assert.Equal("gemini-3.7-flash", balancedIvy);
        Assert.Equal("gemini-3.7-flash", quickIvy);

        var (deepAnt, balancedAnt, quickAnt) = ModelProfileSelector.SelectDefaults(null, isAnthropic: true);
        Assert.Equal("claude-opus-5", deepAnt);
        Assert.Equal("claude-sonnet-5", balancedAnt);
        Assert.Equal("claude-haiku-4-5", quickAnt);

        var (deepGoogle, balancedGoogle, quickGoogle) = ModelProfileSelector.SelectDefaults(null, isGoogle: true);
        Assert.Equal("gemini-3.7-flash", deepGoogle);
        Assert.Equal("gemini-3.7-flash", balancedGoogle);
        Assert.Equal("gemini-3.7-flash", quickGoogle);

        var (deepOpenAi, balancedOpenAi, quickOpenAi) = ModelProfileSelector.SelectDefaults(null, isOpenAi: true);
        Assert.Equal("gpt-5.6-sol", deepOpenAi);
        Assert.Equal("gpt-5.6-terra", balancedOpenAi);
        Assert.Equal("gpt-5.6-luna", quickOpenAi);
    }

    [Fact]
    public void SelectDefaults_ProviderEnum_SelectsAccurately()
    {
        var (deepBerget, balancedBerget, quickBerget) = ModelProfileSelector.SelectDefaults(null, ModelProviderKind.Berget);
        Assert.Equal("moonshotai/Kimi-K3", deepBerget);
        Assert.Equal("moonshotai/Kimi-K3", balancedBerget);
        Assert.Equal("moonshotai/Kimi-K3", quickBerget);

        var (deepCode, balancedCode, quickCode) = ModelProfileSelector.SelectDefaults(null, ModelProviderKind.OpenCode);
        Assert.Equal("claude-opus-5", deepCode);
        Assert.Equal("gemini-3.7-flash", balancedCode);
        Assert.Equal("gemini-3.7-flash", quickCode);
    }

    [Fact]
    public void DetectProvider_IdentifiesFromUrl()
    {
        Assert.Equal(ModelProviderKind.Ivy, ModelProfileSelector.DetectProvider("https://llmproxy.ivy.app/v1"));
        Assert.Equal(ModelProviderKind.Anthropic, ModelProfileSelector.DetectProvider("https://api.anthropic.com/v1"));
        Assert.Equal(ModelProviderKind.Google, ModelProfileSelector.DetectProvider("https://generativelanguage.googleapis.com/v1beta"));
        Assert.Equal(ModelProviderKind.Berget, ModelProfileSelector.DetectProvider("https://api.berget.ai/v1"));
        Assert.Equal(ModelProviderKind.OpenAi, ModelProfileSelector.DetectProvider("https://api.openai.com/v1"));
        Assert.Equal(ModelProviderKind.Generic, ModelProfileSelector.DetectProvider("https://custom.proxy.local/v1"));
    }
}

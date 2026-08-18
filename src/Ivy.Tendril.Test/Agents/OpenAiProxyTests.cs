using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Ivy;
using Ivy.Tendril.Agents.Providers.OpenAiProxy;

namespace Ivy.Tendril.Test.Agents;

public class OpenAiProxyTests
{
    [Fact]
    public void OpenAiProxyCli_BuildProcessSpec_SetsCustomBaseUrlAndApiKey()
    {
        var cli = new OpenAiProxyCli(
            apiKeyProvider: () => "test-sk-key",
            baseUrlProvider: () => "https://custom.openai.proxy/v1");

        var config = new AgentLaunchConfig
        {
            Prompt = "Hello test",
            WorkingDirectory = Path.GetTempPath(),
        };

        var spec = cli.BuildProcessSpec(config);

        Assert.Equal("openaiproxy", cli.Id);
        Assert.Equal("OpenAI Proxy", cli.DisplayName);
        Assert.Equal("https://custom.openai.proxy/v1", spec.Environment["ANTHROPIC_BASE_URL"]);
        Assert.Equal("test-sk-key", spec.Environment["ANTHROPIC_API_KEY"]);
    }

    [Fact]
    public async Task OpenAiProxyHealthCheck_CheckAuthAsync_RequiresBaseUrlAndApiKey()
    {
        var noBaseUrl = new OpenAiProxyHealthCheck(apiKeyProvider: () => "key", baseUrlProvider: () => null);
        var noKey = new OpenAiProxyHealthCheck(apiKeyProvider: () => null, baseUrlProvider: () => "https://api.openai.com");
        var valid = new OpenAiProxyHealthCheck(apiKeyProvider: () => "key", baseUrlProvider: () => "https://api.openai.com");

        var authResultNoUrl = await noBaseUrl.CheckAuthAsync();
        var authResultNoKey = await noKey.CheckAuthAsync();
        var authResultValid = await valid.CheckAuthAsync();

        Assert.Equal(AuthStatus.NotAuthenticated, authResultNoUrl.Status);
        Assert.Equal(AuthStatus.NotAuthenticated, authResultNoKey.Status);
        Assert.Equal(AuthStatus.Authenticated, authResultValid.Status);
    }

    [Fact]
    public void IvyCli_BuildProcessSpec_HardcodesIvyProxyUrl()
    {
        var ivyCli = new IvyCli(apiKeyProvider: () => "ivy-key");
        var config = new AgentLaunchConfig
        {
            Prompt = "Hello test",
            WorkingDirectory = Path.GetTempPath(),
        };

        var spec = ivyCli.BuildProcessSpec(config);

        Assert.Equal("ivy", ivyCli.Id);
        Assert.Equal("https://llmproxy.ivy.app", spec.Environment["ANTHROPIC_BASE_URL"]);
        Assert.Equal("ivy-key", spec.Environment["ANTHROPIC_API_KEY"]);
    }

    [Fact]
    public void IvyModelCatalog_GetStaticModels_ReturnsStandardModels()
    {
        var catalog = new IvyModelCatalog();
        var models = catalog.GetStaticModels();

        Assert.Contains(models, m => m.Id == "claude-opus-5");
        Assert.Contains(models, m => m.Id == "gemini-3.7-flash");
        Assert.Contains(models, m => m.Id == "gpt-5.6-sol");
    }

    [Fact]
    public void OpenAiProxyModelCatalog_GetModelsForBaseUrl_ReturnsProviderSpecificModels()
    {
        var ivyModels = OpenAiProxyModelCatalog.GetModelsForBaseUrl("https://llmproxy.ivy.app");
        Assert.Contains(ivyModels, m => m.Id == "claude-opus-5");
        Assert.Contains(ivyModels, m => m.Id == "gemini-3.7-flash");
        Assert.Contains(ivyModels, m => m.Id == "gpt-5.6-sol");

        var anthropicModels = OpenAiProxyModelCatalog.GetModelsForBaseUrl("https://api.anthropic.com/v1");
        Assert.Contains(anthropicModels, m => m.Id == "claude-opus-5");
        Assert.Contains(anthropicModels, m => m.Id == "claude-opus-4-8");
        Assert.Contains(anthropicModels, m => m.Id == "claude-opus-4-7");
        Assert.Contains(anthropicModels, m => m.Id == "claude-opus-4-6");
        Assert.Contains(anthropicModels, m => m.Id == "claude-sonnet-5");
        Assert.Contains(anthropicModels, m => m.Id == "claude-sonnet-4-6");

        var googleModels = OpenAiProxyModelCatalog.GetModelsForBaseUrl("https://generativelanguage.googleapis.com/v1beta");
        Assert.Contains(googleModels, m => m.Id == "gemini-3.7-flash");
        Assert.Contains(googleModels, m => m.Id == "gemini-3.6-flash");

        var openaiModels = OpenAiProxyModelCatalog.GetModelsForBaseUrl("https://api.openai.com");
        Assert.Contains(openaiModels, m => m.Id == "gpt-5.6-sol");
        Assert.Contains(openaiModels, m => m.Id == "gpt-5.6-terra");

        var bergetModels = OpenAiProxyModelCatalog.GetModelsForBaseUrl("https://api.berget.ai/v1");
        Assert.Contains(bergetModels, m => m.Id == "moonshotai/Kimi-K3");

        var customModels = OpenAiProxyModelCatalog.GetModelsForBaseUrl("https://custom-proxy.internal");
        Assert.Contains(customModels, m => m.Id == "claude-opus-4-8");
        Assert.Contains(customModels, m => m.Id == "claude-opus-4-7");
        Assert.Contains(customModels, m => m.Id == "claude-sonnet-4-6");
        Assert.Contains(customModels, m => m.Id == "gemini-3.7-flash");
        Assert.Contains(customModels, m => m.Id == "gpt-5.6-sol");
    }

    [Fact]
    public void IvyCli_DefaultProfiles_ReturnsIvyModels()
    {
        var cli = new IvyCli();
        var defaults = cli.DefaultProfiles;

        var deep = defaults.First(p => p.Tier == ProfileTier.Deep);
        var balanced = defaults.First(p => p.Tier == ProfileTier.Balanced);
        var quick = defaults.First(p => p.Tier == ProfileTier.Quick);

        Assert.Equal("claude-opus-5", deep.Model);
        Assert.Equal("max", deep.Effort);
        Assert.Equal("gemini-3.7-flash", balanced.Model);
        Assert.Equal("medium", balanced.Effort);
        Assert.Equal("gemini-3.7-flash", quick.Model);
        Assert.Equal("low", quick.Effort);
    }

    [Fact]
    public void OpenAiProxyCli_DefaultProfiles_FollowsBaseUrl()
    {
        var ivyCli = new OpenAiProxyCli(baseUrlProvider: () => "https://llmproxy.ivy.app");
        var ivyDeep = ivyCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Deep);
        var ivyBalanced = ivyCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Balanced);
        var ivyQuick = ivyCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Quick);

        Assert.Equal("claude-opus-5", ivyDeep.Model);
        Assert.Equal("max", ivyDeep.Effort);
        Assert.Equal("gemini-3.7-flash", ivyBalanced.Model);
        Assert.Equal("medium", ivyBalanced.Effort);
        Assert.Equal("gemini-3.7-flash", ivyQuick.Model);
        Assert.Equal("low", ivyQuick.Effort);

        var anthropicCli = new OpenAiProxyCli(baseUrlProvider: () => "https://api.anthropic.com/v1");
        Assert.Equal("claude-opus-5", anthropicCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Deep).Model);

        var openaiCli = new OpenAiProxyCli(baseUrlProvider: () => "https://api.openai.com");
        Assert.Equal("gpt-5.6-sol", openaiCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Deep).Model);

        var googleCli = new OpenAiProxyCli(baseUrlProvider: () => "https://generativelanguage.googleapis.com/v1beta");
        var googleDeep = googleCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Deep);
        var googleBalanced = googleCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Balanced);
        var googleQuick = googleCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Quick);

        Assert.Equal("gemini-3.7-flash", googleDeep.Model);
        Assert.Equal("high", googleDeep.Effort);
        Assert.Equal("gemini-3.7-flash", googleBalanced.Model);
        Assert.Equal("medium", googleBalanced.Effort);
        Assert.Equal("gemini-3.7-flash", googleQuick.Model);
        Assert.Equal("medium", googleQuick.Effort);
    }

    [Fact]
    public async Task OpenAiProxyModelCatalog_FetchModelsFromEndpointAsync_ReturnsStaticFallbackWhenOffline()
    {
        var models = await OpenAiProxyModelCatalog.FetchModelsFromEndpointAsync("http://127.0.0.1:54321/v1", "sk-test");
        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.Id == "gpt-5.6-sol");
    }

    [Fact]
    public async Task OpenAiProxyModelCatalog_TestModelEndpointAsync_RejectsInvalidModelName()
    {
        var (okEmpty, errEmpty) = await OpenAiProxyModelCatalog.TestModelEndpointAsync("https://api.openai.com", "sk-test", "");
        var (okCustom, errCustom) = await OpenAiProxyModelCatalog.TestModelEndpointAsync("https://api.openai.com", "sk-test", "__custom__");

        Assert.False(okEmpty);
        Assert.False(okCustom);
        Assert.Contains("valid", errEmpty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valid", errCustom, StringComparison.OrdinalIgnoreCase);
    }
}

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
    public void IvyModelCatalog_GetStaticModels_ReturnsIvyModelsOnly()
    {
        var catalog = new IvyModelCatalog();
        var models = catalog.GetStaticModels();

        Assert.Contains(models, m => m.Id == "ivy-stem" && m.DisplayName == "Ivy Stem");
        Assert.Contains(models, m => m.Id == "ivy-root" && m.DisplayName == "Ivy Root");
        Assert.Contains(models, m => m.Id == "ivy-leaf" && m.DisplayName == "Ivy Leaf");
        Assert.All(models, m => Assert.True(m.Id.StartsWith("ivy") || m.Id == "default"));
    }

    [Fact]
    public void OpenAiProxyModelCatalog_GetModelsForBaseUrl_ReturnsProviderSpecificModels()
    {
        var ivyModels = OpenAiProxyModelCatalog.GetModelsForBaseUrl("https://llmproxy.ivy.app");
        Assert.Contains(ivyModels, m => m.Id == "ivy-stem");
        Assert.Contains(ivyModels, m => m.Id == "ivy-root");
        Assert.Contains(ivyModels, m => m.Id == "ivy-leaf");

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

        Assert.Equal("ivy-stem", defaults.First(p => p.Tier == ProfileTier.Deep).Model);
        Assert.Equal("ivy-root", defaults.First(p => p.Tier == ProfileTier.Balanced).Model);
        Assert.Equal("ivy-leaf", defaults.First(p => p.Tier == ProfileTier.Quick).Model);
    }

    [Fact]
    public void OpenAiProxyCli_DefaultProfiles_FollowsBaseUrl()
    {
        var ivyCli = new OpenAiProxyCli(baseUrlProvider: () => "https://llmproxy.ivy.app");
        Assert.Equal("ivy-stem", ivyCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Deep).Model);

        var anthropicCli = new OpenAiProxyCli(baseUrlProvider: () => "https://api.anthropic.com/v1");
        Assert.Equal("claude-opus-5", anthropicCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Deep).Model);

        var openaiCli = new OpenAiProxyCli(baseUrlProvider: () => "https://api.openai.com");
        Assert.Equal("gpt-5.6-sol", openaiCli.DefaultProfiles.First(p => p.Tier == ProfileTier.Deep).Model);
    }
}

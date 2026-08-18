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
}

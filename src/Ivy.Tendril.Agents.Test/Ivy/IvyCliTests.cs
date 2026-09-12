using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Ivy;

namespace Ivy.Tendril.Agents.Test.Providers.Ivy;

public class IvyCliTests
{
    private readonly IvyCli _cli = new();

    [Fact]
    public void BuildProcessSpec_PreservesOpenCodeConfigContent()
    {
        var config = new AgentLaunchConfig
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
            Model = "claude-opus-5",
        };

        var spec = _cli.BuildProcessSpec(config);

        Assert.Equal("ivy-agent", Path.GetFileNameWithoutExtension(spec.FileName));
        Assert.True(spec.Environment.TryGetValue("OPENCODE_CONFIG_CONTENT", out var configContent));
        Assert.Contains("\"output\":128000", configContent);
    }
}

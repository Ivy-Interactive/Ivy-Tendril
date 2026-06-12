using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Copilot;

namespace Ivy.Tendril.Agents.Test.Copilot;

public class CopilotPtyTests
{
    private readonly CopilotPty _pty = new();

    [Fact]
    public void BuildPtySpec_FullAuto_GrantsAllToolsPathsAndUrls()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.FullAuto,
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.Contains("--allow-all-tools", spec.CommandLine);
        Assert.Contains("--allow-all-paths", spec.CommandLine);
        Assert.Contains("--allow-all-urls", spec.CommandLine);
    }

    [Fact]
    public void BuildPtySpec_DefaultPermission_OmitsAllowAllFlags()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.Default,
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.DoesNotContain("--allow-all-tools", spec.CommandLine);
        Assert.DoesNotContain("--allow-all-paths", spec.CommandLine);
        Assert.DoesNotContain("--allow-all-urls", spec.CommandLine);
    }
}

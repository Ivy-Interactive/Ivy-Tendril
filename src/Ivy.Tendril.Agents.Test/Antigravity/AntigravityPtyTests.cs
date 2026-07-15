using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;

namespace Ivy.Tendril.Agents.Test.Antigravity;

public class AntigravityPtyTests
{
    private readonly AntigravityPty _pty = new();

    [Fact]
    public void BuildPtySpec_FullAuto_SkipsPermissions()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.FullAuto,
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.Contains("--dangerously-skip-permissions", spec.CommandLine);
    }

    [Fact]
    public void BuildPtySpec_InitialPrompt_PassedViaInteractiveFlag()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            InitialPrompt = "do the thing",
        };

        var args = _pty.BuildPtySpec(config).CommandLine.ToList();

        var idx = args.IndexOf("-i");
        Assert.True(idx >= 0, "Expected -i flag.");
        Assert.Equal("do the thing", args[idx + 1]);
    }

    [Fact]
    public void BuildPtySpec_NoInitialPrompt_OmitsInteractiveFlag()
    {
        var config = new AgentPtyConfig { WorkingDirectory = "/tmp/test" };

        var spec = _pty.BuildPtySpec(config);

        Assert.DoesNotContain("-i", spec.CommandLine);
    }

    [Fact]
    public void ContextFileName_IsAgentsMd()
    {
        Assert.Equal("AGENTS.md", _pty.ContextFileName);
    }
}

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

    [Fact]
    public void GetActivityPatterns_HasTrustPrompt()
    {
        var patterns = _pty.GetActivityPatterns()!;
        Assert.False(string.IsNullOrEmpty(patterns.TrustPromptPattern));
        Assert.Equal("\r", patterns.TrustAcceptInput);
    }
}

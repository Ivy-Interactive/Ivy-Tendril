using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Codex;

namespace Ivy.Tendril.Agents.Test.Codex;

public class CodexPtyTests
{
    private readonly CodexPty _pty = new();

    [Fact]
    public void BuildPtySpec_FullAuto_UsesSandboxAndApprovalFlags()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.FullAuto,
        };

        var spec = _pty.BuildPtySpec(config);

        // The interactive TUI has no `--full-auto` shorthand.
        Assert.DoesNotContain("--full-auto", spec.CommandLine);
        AssertFlagValue(spec.CommandLine.ToList(), "--sandbox", "workspace-write");
        AssertFlagValue(spec.CommandLine.ToList(), "--ask-for-approval", "never");
    }

    [Fact]
    public void BuildPtySpec_DefaultPermission_OmitsApprovalFlags()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.Default,
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.DoesNotContain("--full-auto", spec.CommandLine);
        Assert.DoesNotContain("--sandbox", spec.CommandLine);
        Assert.DoesNotContain("--ask-for-approval", spec.CommandLine);
    }

    [Fact]
    public void BuildPtySpec_InitialPrompt_IsTrailingPositionalArg()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.FullAuto,
            InitialPrompt = "do the thing",
        };

        var args = _pty.BuildPtySpec(config).CommandLine.ToList();

        // The Codex TUI takes [PROMPT] as the final positional argument (after all options).
        Assert.Equal("do the thing", args[^1]);
    }

    [Fact]
    public void BuildPtySpec_NoInitialPrompt_HasNoTrailingPrompt()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.Default,
        };

        var args = _pty.BuildPtySpec(config).CommandLine.ToList();

        Assert.Equal("codex", args[^1]);
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

    private static void AssertFlagValue(List<string> args, string flag, string expectedValue)
    {
        var idx = args.IndexOf(flag);
        Assert.True(idx >= 0, $"Expected flag {flag} to be present.");
        Assert.True(idx + 1 < args.Count, $"Expected a value after {flag}.");
        Assert.Equal(expectedValue, args[idx + 1]);
    }
}

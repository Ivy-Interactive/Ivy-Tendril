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

    private static void AssertFlagValue(List<string> args, string flag, string expectedValue)
    {
        var idx = args.IndexOf(flag);
        Assert.True(idx >= 0, $"Expected flag {flag} to be present.");
        Assert.True(idx + 1 < args.Count, $"Expected a value after {flag}.");
        Assert.Equal(expectedValue, args[idx + 1]);
    }
}

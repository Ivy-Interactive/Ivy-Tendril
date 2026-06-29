using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.OpenCode;

namespace Ivy.Tendril.Agents.Test.OpenCode;

public class OpenCodePtyTests
{
    private readonly OpenCodePty _pty = new();

    [Fact]
    public void BuildPtySpec_FullAuto_OmitsSkipPermissions()
    {
        // The interactive TUI rejects --dangerously-skip-permissions (it only
        // exists on the `run` subcommand), so FullAuto must not add it.
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.FullAuto,
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.DoesNotContain("--dangerously-skip-permissions", spec.CommandLine);
    }

    [Fact]
    public void BuildPtySpec_DefaultPermission_OmitsSkipPermissions()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.Default,
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.DoesNotContain("--dangerously-skip-permissions", spec.CommandLine);
    }
}

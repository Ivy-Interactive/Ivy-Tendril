using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Gemini;

namespace Ivy.Tendril.Agents.Test.Gemini;

public class GeminiPtyTests
{
    private readonly GeminiPty _pty = new();

    [Fact]
    public void BuildPtySpec_FullAuto_UsesYoloFlag()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.FullAuto,
        };

        var spec = _pty.BuildPtySpec(config);

        // The interactive TUI uses --yolo; --approval-mode is headless-only and
        // missing from older gemini builds.
        Assert.Contains("--yolo", spec.CommandLine);
        Assert.DoesNotContain("--approval-mode", spec.CommandLine);
    }

    [Fact]
    public void BuildPtySpec_DefaultPermission_OmitsYoloFlag()
    {
        var config = new AgentPtyConfig
        {
            WorkingDirectory = "/tmp/test",
            PermissionMode = PermissionMode.Default,
        };

        var spec = _pty.BuildPtySpec(config);

        Assert.DoesNotContain("--yolo", spec.CommandLine);
    }
}

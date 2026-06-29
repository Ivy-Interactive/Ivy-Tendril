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

    [Fact]
    public void BuildPtySpec_AlwaysAddsSkipTrust()
    {
        var config = new AgentPtyConfig { WorkingDirectory = "/tmp/test" };

        var spec = _pty.BuildPtySpec(config);

        Assert.Contains("--skip-trust", spec.CommandLine);
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
    public void ContextFileName_IsGeminiMd()
    {
        Assert.Equal("GEMINI.md", _pty.ContextFileName);
    }
}

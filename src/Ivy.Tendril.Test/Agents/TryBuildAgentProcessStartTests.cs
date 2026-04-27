using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Test.Agents;

public class TryBuildAgentProcessStartTests : IDisposable
{
    private readonly string _promptsDir;
    private readonly string _tempDir;

    public TryBuildAgentProcessStartTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agent-launch-test-{Guid.NewGuid():N}");
        _promptsDir = Path.Combine(_tempDir, "Promptwares");
        Directory.CreateDirectory(_promptsDir);

    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void FirmwareCompiler_Compile_IncludesReflection()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            "/programs/Test/Logs/00001.md",
            new Dictionary<string, string>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("Reflection", result);
        Assert.Contains("Memory/", result);
        Assert.Contains("improve over time", result);
    }

    [Fact]
    public void FirmwareCompiler_Compile_IncludesToolsInstructions()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            "/programs/Test/Logs/00001.md",
            new Dictionary<string, string>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("Tools/", result);
        Assert.Contains("reusable tools", result);
    }

    [Fact]
    public void AgentProviderFactory_Resolve_NormalizesToolPaths()
    {
        var settings = new TendrilSettings
        {
            CodingAgent = "claude",
            Promptwares = new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new()
                {
                    AllowedTools = new List<string> { @"D:\Repos\Tools\script.ps1" }
                }
            },
            CodingAgents = new List<AgentConfig>()
        };

        var resolution = AgentProviderFactory.Resolve(settings, "Test");

        // Backslashes should be converted to forward slashes
        Assert.Equal("D:/Repos/Tools/script.ps1", resolution.AllowedTools[0]);
    }

    [Fact]
    public void ClaudeProvider_ExtractResult_HandlesMultipleResults()
    {
        var provider = new ClaudeAgentProvider();
        var lines = new List<string>
        {
            "{\"type\":\"result\",\"result\":\"first result\"}",
            "{\"type\":\"status\",\"message\":\"working\"}",
            "{\"type\":\"result\",\"result\":\"final result\"}"
        };

        // Should return the LAST result
        var result = provider.ExtractResult(lines);
        Assert.Equal("final result", result);
    }

    [Fact]
    public void ClaudeProvider_ExtractResult_HandlesmalformedJson()
    {
        var provider = new ClaudeAgentProvider();
        var lines = new List<string>
        {
            "not json at all",
            "{\"type\":\"result\",\"result\":\"actual result\"}",
            "garbage after result"
        };

        var result = provider.ExtractResult(lines);
        Assert.Equal("actual result", result);
    }

    [Fact]
    public void CodexProvider_ExtractResult_ReturnsLastNonEmpty()
    {
        var provider = new CodexAgentProvider();
        var lines = new List<string>
        {
            "Working on the task...",
            "Applying changes...",
            "Done! All changes applied.",
            "",
            ""
        };

        var result = provider.ExtractResult(lines);
        Assert.Equal("Done! All changes applied.", result);
    }

    [Fact]
    public void AgentProviderFactory_Resolve_EmptyAllowedToolsFromSpecificDoesNotOverride()
    {
        var settings = new TendrilSettings
        {
            CodingAgent = "claude",
            Promptwares = new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new()
                {
                    Profile = "balanced",
                    AllowedTools = new List<string> { "Read", "Write", "Bash" }
                },
                ["SimpleTask"] = new()
                {
                    Profile = "quick",
                    AllowedTools = new List<string>() // empty — should NOT override
                }
            },
            CodingAgents = new List<AgentConfig>
            {
                new()
                {
                    Name = "claude",
                    Profiles = new List<AgentProfileConfig>
                    {
                        new() { Name = "balanced", Model = "sonnet" },
                        new() { Name = "quick", Model = "haiku" }
                    }
                }
            }
        };

        var resolution = AgentProviderFactory.Resolve(settings, "SimpleTask");

        // Empty tools from specific config should NOT replace default tools
        Assert.Equal(new[] { "Read", "Write", "Bash" }, resolution.AllowedTools);
        Assert.Equal("haiku", resolution.Model);
    }

    [Fact]
    public void FirmwareCompiler_GetNextLogFile_HandlesNonNumericFiles()
    {
        var programFolder = Path.Combine(_tempDir, "TestProgram");
        var logsFolder = Path.Combine(programFolder, "Logs");
        Directory.CreateDirectory(logsFolder);

        // Create files that don't match the numeric pattern
        File.WriteAllText(Path.Combine(logsFolder, "readme.md"), "ignore me");
        File.WriteAllText(Path.Combine(logsFolder, "00003.md"), "log 3");

        var logFile = FirmwareCompiler.GetNextLogFile(programFolder);
        Assert.EndsWith("00004.md", logFile);
    }

    [Fact]
    public void ClaudeProvider_BuildProcessStart_WorkingDirectory()
    {
        var provider = new ClaudeAgentProvider();
        var invocation = new AgentInvocation(
            "prompt", "/work/dir", "sonnet", "high", "sess-1",
            Array.Empty<string>(), Array.Empty<string>());

        var psi = provider.BuildProcessStart(invocation);
        Assert.Equal("/work/dir", psi.WorkingDirectory);
    }

    [Fact]
    public void ClaudeProvider_BuildProcessStart_RedirectsAllStreams()
    {
        var provider = new ClaudeAgentProvider();
        var invocation = new AgentInvocation(
            "prompt", "/work", "", "", "",
            Array.Empty<string>(), Array.Empty<string>());

        var psi = provider.BuildProcessStart(invocation);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.True(psi.RedirectStandardInput);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.CreateNoWindow);
    }
}
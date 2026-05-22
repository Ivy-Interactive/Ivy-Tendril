using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Services;

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

    private static IAgentRunner CreateRunner() => TestAgentRunner.Create();

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
            new Dictionary<string, string>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("Reflection", result);
        Assert.Contains("write-memory", result);
        Assert.Contains("improve over time", result);
    }

    [Fact]
    public void FirmwareCompiler_Compile_IncludesToolsInstructions()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            new Dictionary<string, string>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("**Tools:**", result);
        Assert.Contains("**Memory:**", result);
    }

    [Fact]
    public void AgentProviderFactory_Resolve_NormalizesToolPaths()
    {
        var runner = CreateRunner();
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

        var resolution = AgentProviderFactory.Resolve(runner, settings, "Test");

        // Backslashes should be converted to forward slashes
        Assert.Contains("D:/Repos/Tools/script.ps1", resolution.AllowedTools);
    }


    [Fact]
    public void AgentProviderFactory_Resolve_EmptyAllowedToolsFromSpecificDoesNotOverride()
    {
        var runner = CreateRunner();
        var settings = new TendrilSettings
        {
            CodingAgent = "claude",
            Promptwares = new Dictionary<string, PromptwareConfig>
            {
                ["_default"] = new()
                {
                    Profile = "balanced",
                    AllowedTools = new List<string> { "Write" }
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

        var resolution = AgentProviderFactory.Resolve(runner, settings, "SimpleTask");

        // Base tools + _default's Write should still be present
        Assert.Contains("Read", resolution.AllowedTools);
        Assert.Contains("Write", resolution.AllowedTools);
        Assert.Contains("Bash", resolution.AllowedTools);
        Assert.Equal("haiku", resolution.Model);
    }

    [Fact]
    public void FirmwareCompiler_GetLogFile_CreatesNamedLogFile()
    {
        var programFolder = Path.Combine(_tempDir, "TestProgram");
        Directory.CreateDirectory(programFolder);

        var logFile = FirmwareCompiler.GetLogFile(programFolder, "00099");
        Assert.EndsWith("00099.md", logFile);
        Assert.True(File.Exists(logFile));
    }

}
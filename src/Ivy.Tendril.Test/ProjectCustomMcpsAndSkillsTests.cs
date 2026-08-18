using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using System.Text.Json;
using Xunit;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class ProjectCustomMcpsAndSkillsTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-custom-mcps-skills-test");
    private readonly string _originalTendrilHome;

    public ProjectCustomMcpsAndSkillsTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        var yaml = @"
projects: []
verifications: []
";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), yaml);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private ConfigService CreateConfig() => new();

    [Fact]
    public void SaveAndReload_ProjectWithMcpServersAndSkills_PersistsCorrectly()
    {
        var config = CreateConfig();
        var project = new ProjectConfig
        {
            Name = "McpSkillProject",
            Color = "Emerald",
            Context = "Test context for custom MCPs and Skills",
            McpServers = new List<ProjectMcpServerRef>
            {
                new()
                {
                    Name = "sqlite-db",
                    Command = "npx",
                    Arguments = new List<string> { "-y", "@modelcontextprotocol/server-sqlite", "--db-path", "test.db" },
                    Environment = new Dictionary<string, string> { { "DB_TIMEOUT", "30" } },
                    Disabled = false
                }
            },
            Skills = new List<ProjectSkillRef>
            {
                new()
                {
                    Name = "code-reviewer",
                    Description = "Reviews code changes against team conventions",
                    Instructions = "Always check for non-blocking I/O and proper error handling.",
                    Disabled = false
                }
            }
        };

        config.Settings.Projects.Add(project);
        config.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Projects);
        var loadedProj = reloaded.Settings.Projects[0];

        Assert.Equal("McpSkillProject", loadedProj.Name);
        Assert.Single(loadedProj.McpServers);
        Assert.Equal("sqlite-db", loadedProj.McpServers[0].Name);
        Assert.Equal("npx", loadedProj.McpServers[0].Command);
        Assert.Equal(4, loadedProj.McpServers[0].Arguments.Count);
        Assert.Equal("30", loadedProj.McpServers[0].Environment["DB_TIMEOUT"]);

        Assert.Single(loadedProj.Skills);
        Assert.Equal("code-reviewer", loadedProj.Skills[0].Name);
        Assert.Equal("Reviews code changes against team conventions", loadedProj.Skills[0].Description);
        Assert.Equal("Always check for non-blocking I/O and proper error handling.", loadedProj.Skills[0].Instructions);
    }

    [Fact]
    public void FirmwareCompiler_RendersProjectSkills_IntoCompiledPrompt()
    {
        var projectInfo = new ProjectInfo(
            Name: "SkillProject",
            Context: "Project description",
            Repos: new List<ProjectRepoInfo>(),
            Verifications: new List<ProjectVerificationInfo>(),
            Skills: new List<ProjectSkillInfo>
            {
                new("db-migration-guide", "Guide for running migrations safely", "Run dotnet ef database update before starting.")
            }
        );

        var firmwareCtx = new FirmwareContext(
            ProgramFolder: _tempDir.Path,
            Values: new Dictionary<string, string>(),
            Projects: new[] { projectInfo }
        );

        var compiledPrompt = FirmwareCompiler.Compile(firmwareCtx);

        Assert.Contains("### SkillProject", compiledPrompt);
        Assert.Contains("**Skills:**", compiledPrompt);
        Assert.Contains("#### Skill: db-migration-guide", compiledPrompt);
        Assert.Contains("Guide for running migrations safely", compiledPrompt);
        Assert.Contains("Run dotnet ef database update before starting.", compiledPrompt);
    }

    [Fact]
    public void ProjectPathHelper_ResolvesAndCreatesDirectoryStructureCorrectly()
    {
        var tendrilHome = _tempDir.Path;
        var projectName = "DemoProject";

        var root = ProjectPathHelper.GetProjectRoot(tendrilHome, projectName);
        var repos = ProjectPathHelper.GetReposDir(tendrilHome, projectName);
        var repoPath = ProjectPathHelper.GetRepoPath(tendrilHome, projectName, "Ivy-Interactive", "Ivy-Tendril");
        var skills = ProjectPathHelper.GetSkillsDir(tendrilHome, projectName);
        var mcp = ProjectPathHelper.GetMcpDir(tendrilHome, projectName);
        var memory = ProjectPathHelper.GetMemoryDir(tendrilHome, projectName);

        Assert.EndsWith(Path.Combine("Projects", "DemoProject"), root);
        Assert.EndsWith(Path.Combine("Projects", "DemoProject", "Repos"), repos);
        Assert.EndsWith(Path.Combine("Projects", "DemoProject", "Repos", "Ivy-Interactive", "Ivy-Tendril"), repoPath);
        Assert.EndsWith(Path.Combine("Projects", "DemoProject", "Skills"), skills);
        Assert.EndsWith(Path.Combine("Projects", "DemoProject", "MCP"), mcp);
        Assert.EndsWith(Path.Combine("Projects", "DemoProject", "Memory"), memory);

        ProjectPathHelper.EnsureProjectDirectories(tendrilHome, projectName);

        Assert.True(Directory.Exists(root));
        Assert.True(Directory.Exists(repos));
        Assert.True(Directory.Exists(skills));
        Assert.True(Directory.Exists(mcp));
        Assert.True(Directory.Exists(memory));
    }

    [Fact]
    public void FirmwareCompiler_RendersProjectMemory_IntoCompiledPrompt()
    {
        var projectInfo = new ProjectInfo(
            Name: "MemoryProject",
            Context: "Project context",
            Repos: new List<ProjectRepoInfo>(),
            Verifications: new List<ProjectVerificationInfo>(),
            Memories: new List<ProjectMemoryInfo>
            {
                new("stack.md", "Framework: .NET 10\nUI: Ivy Framework"),
                new("conventions.md", "Never hardcode margins or padding in C# views.")
            }
        );

        var firmwareCtx = new FirmwareContext(
            ProgramFolder: _tempDir.Path,
            Values: new Dictionary<string, string>(),
            Projects: new[] { projectInfo }
        );

        var compiledPrompt = FirmwareCompiler.Compile(firmwareCtx);

        Assert.Contains("### MemoryProject", compiledPrompt);
        Assert.Contains("**Memory:**", compiledPrompt);
        Assert.Contains("#### Memory: stack.md", compiledPrompt);
        Assert.Contains("Framework: .NET 10", compiledPrompt);
        Assert.Contains("#### Memory: conventions.md", compiledPrompt);
        Assert.Contains("Never hardcode margins or padding in C# views.", compiledPrompt);
    }

    [Fact]
    public void ProjectPathHelper_MoveProjectDirectory_PreservesSubdirectoriesAndFiles()
    {
        var tendrilHome = _tempDir.Path;
        var oldName = "OldProject";
        var newName = "RenamedProject";

        ProjectPathHelper.EnsureProjectDirectories(tendrilHome, oldName);

        var oldMemoryDir = ProjectPathHelper.GetMemoryDir(tendrilHome, oldName);
        var oldMemoryFile = Path.Combine(oldMemoryDir, "stack.md");
        File.WriteAllText(oldMemoryFile, "Stack details");

        ProjectPathHelper.MoveProjectDirectory(tendrilHome, oldName, newName);

        var oldRoot = ProjectPathHelper.GetProjectRoot(tendrilHome, oldName);
        var newRoot = ProjectPathHelper.GetProjectRoot(tendrilHome, newName);
        var newMemoryFile = Path.Combine(ProjectPathHelper.GetMemoryDir(tendrilHome, newName), "stack.md");

        Assert.False(Directory.Exists(oldRoot));
        Assert.True(Directory.Exists(newRoot));
        Assert.True(File.Exists(newMemoryFile));
        Assert.Equal("Stack details", File.ReadAllText(newMemoryFile));
    }
}

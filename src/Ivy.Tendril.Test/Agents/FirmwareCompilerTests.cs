using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Test.Agents;

public class FirmwareCompilerTests : IDisposable
{
    private readonly string _tempDir;

    public FirmwareCompilerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"firmware-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Compile_InjectsHeaderValues()
    {
        var context = new FirmwareContext(
            "/programs/CreatePlan",
            new Dictionary<string, string> { ["TendrilPlanId"] = "03456", ["TendrilProject"] = "Tendril" });

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("TendrilPlanId: 03456", result);
        Assert.Contains("TendrilProject: Tendril", result);
    }

    [Fact]
    public void Compile_InjectsCurrentTimeIfMissing()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            new Dictionary<string, string>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("CurrentTime:", result);
    }

    [Fact]
    public void Compile_DoesNotOverrideExplicitCurrentTime()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            new Dictionary<string, string> { ["CurrentTime"] = "2026-01-01T00:00:00Z" });

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("CurrentTime: 2026-01-01T00:00:00Z", result);
    }

    [Fact]
    public void Compile_ReplacesProgramFolder()
    {
        var context = new FirmwareContext(
            "/my/programs/ExecutePlan",
            new Dictionary<string, string>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("/my/programs/ExecutePlan", result);
        Assert.DoesNotContain("{PROGRAMFOLDER}", result);
    }

    [Fact]
    public void Compile_DoesNotContainLogFilePlaceholder()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            new Dictionary<string, string>());

        var result = FirmwareCompiler.Compile(context);

        Assert.DoesNotContain("{LOGFILE}", result);
        Assert.DoesNotContain("In the log file you are to maintain", result);
    }

    [Fact]
    public void Compile_AppendsEmbeddedPlansReference()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            new Dictionary<string, string>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("## Reference Documents", result);
        Assert.Contains("### Plans", result);
        Assert.Contains("Plans File Structure", result);
        Assert.Contains("tendril plan", result);
    }

    [Fact]
    public void Compile_HeaderValuesSortedAlphabetically()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            new Dictionary<string, string> { ["Zebra"] = "last", ["Alpha"] = "first" });

        var result = FirmwareCompiler.Compile(context);

        var alphaIdx = result.IndexOf("Alpha: first");
        var zebraIdx = result.IndexOf("Zebra: last");
        Assert.True(alphaIdx < zebraIdx);
    }

    [Fact]
    public void Compile_ContainsCoreInstructions()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            new Dictionary<string, string>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("You are an agentic application", result);
        Assert.Contains("Program.md", result);
        Assert.Contains("Reflection", result);
        Assert.Contains("**Memory:**", result);
        Assert.Contains("**Tools:**", result);
    }

    // --- GetNextLogFile ---

    [Fact]
    public void GetNextLogFile_ReturnsFirst_WhenEmpty()
    {
        var programFolder = Path.Combine(_tempDir, "TestProgram");
        Directory.CreateDirectory(programFolder);

        var logFile = FirmwareCompiler.GetNextLogFile(programFolder);

        Assert.EndsWith("00001.md", logFile);
        Assert.Contains("Logs", logFile);
    }

    [Fact]
    public void GetNextLogFile_IncrementsExisting()
    {
        var programFolder = Path.Combine(_tempDir, "TestProgram");
        var logsFolder = Path.Combine(programFolder, "Logs");
        Directory.CreateDirectory(logsFolder);
        File.WriteAllText(Path.Combine(logsFolder, "00001.md"), "log 1");
        File.WriteAllText(Path.Combine(logsFolder, "00002.md"), "log 2");

        var logFile = FirmwareCompiler.GetNextLogFile(programFolder);

        Assert.EndsWith("00003.md", logFile);
    }

    [Fact]
    public void GetNextLogFile_CreatesLogsDirectory()
    {
        var programFolder = Path.Combine(_tempDir, "NewProgram");
        Directory.CreateDirectory(programFolder);

        FirmwareCompiler.GetNextLogFile(programFolder);

        Assert.True(Directory.Exists(Path.Combine(programFolder, "Logs")));
    }

    [Fact]
    public void GetNextLogFile_ReservesFileOnDisk()
    {
        var programFolder = Path.Combine(_tempDir, "ReserveTest");
        Directory.CreateDirectory(programFolder);

        var logFile = FirmwareCompiler.GetNextLogFile(programFolder);

        Assert.True(File.Exists(logFile));
    }

    [Fact]
    public void GetNextLogFile_ConcurrentCalls_ProduceDifferentNumbers()
    {
        var programFolder = Path.Combine(_tempDir, "ConcurrentTest");
        Directory.CreateDirectory(programFolder);

        var first = FirmwareCompiler.GetNextLogFile(programFolder);
        var second = FirmwareCompiler.GetNextLogFile(programFolder);

        Assert.EndsWith("00001.md", first);
        Assert.EndsWith("00002.md", second);
        Assert.NotEqual(first, second);
    }

    // --- Projects Section ---

    [Fact]
    public void Compile_RendersProjectsSection()
    {
        var projects = new[]
        {
            new ProjectInfo("MyProject", "A test project",
                new List<ProjectRepoInfo> { new("D:\\Repos\\my-app", "org/my-app") },
                new List<ProjectVerificationInfo> { new("Build", true, false), new("Lint", false, true) })
        };

        var context = new FirmwareContext("/programs/Test", new Dictionary<string, string>(), Projects: projects);
        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("## Projects", result);
        Assert.Contains("### MyProject", result);
        Assert.Contains("A test project", result);
        Assert.Contains("org/my-app", result);
        Assert.Contains("Build (required)", result);
        Assert.Contains("Lint (optional, delegated)", result);
    }

    [Fact]
    public void Compile_RendersMultipleProjects()
    {
        var projects = new[]
        {
            new ProjectInfo("Alpha", "", new List<ProjectRepoInfo>(), new List<ProjectVerificationInfo>()),
            new ProjectInfo("Beta", "Beta context", new List<ProjectRepoInfo>(), new List<ProjectVerificationInfo>())
        };

        var context = new FirmwareContext("/programs/Test", new Dictionary<string, string>(), Projects: projects);
        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("### Alpha", result);
        Assert.Contains("### Beta", result);
        Assert.Contains("Beta context", result);
    }

    [Fact]
    public void Compile_OmitsProjectsSection_WhenNull()
    {
        var context = new FirmwareContext("/programs/Test", new Dictionary<string, string>());
        var result = FirmwareCompiler.Compile(context);

        Assert.DoesNotContain("## Projects", result);
    }

    [Fact]
    public void Compile_OmitsProjectsSection_WhenEmpty()
    {
        var context = new FirmwareContext("/programs/Test", new Dictionary<string, string>(), Projects: Array.Empty<ProjectInfo>());
        var result = FirmwareCompiler.Compile(context);

        Assert.DoesNotContain("## Projects", result);
    }

    [Fact]
    public void Compile_ProjectsSectionAppearsBeforeReferenceDocuments()
    {
        var projects = new[]
        {
            new ProjectInfo("TestProj", "ctx", new List<ProjectRepoInfo>(), new List<ProjectVerificationInfo>())
        };

        var context = new FirmwareContext("/programs/Test", new Dictionary<string, string>(), Projects: projects);
        var result = FirmwareCompiler.Compile(context);

        var projectsIdx = result.IndexOf("## Projects");
        var referenceIdx = result.IndexOf("## Reference Documents");
        Assert.True(projectsIdx < referenceIdx);
    }
}
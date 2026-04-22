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
            "/programs/CreatePlan/Logs/00001.md",
            new Dictionary<string, string> { ["PlanId"] = "03456", ["Project"] = "Tendril" },
            new List<(string Name, string Content)>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("PlanId: 03456", result);
        Assert.Contains("Project: Tendril", result);
    }

    [Fact]
    public void Compile_InjectsCurrentTimeIfMissing()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            "/programs/Test/Logs/00001.md",
            new Dictionary<string, string>(),
            new List<(string Name, string Content)>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("CurrentTime:", result);
    }

    [Fact]
    public void Compile_DoesNotOverrideExplicitCurrentTime()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            "/programs/Test/Logs/00001.md",
            new Dictionary<string, string> { ["CurrentTime"] = "2026-01-01T00:00:00Z" },
            new List<(string Name, string Content)>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("CurrentTime: 2026-01-01T00:00:00Z", result);
    }

    [Fact]
    public void Compile_ReplacesProgramFolder()
    {
        var context = new FirmwareContext(
            "/my/programs/ExecutePlan",
            "/my/programs/ExecutePlan/Logs/00003.md",
            new Dictionary<string, string>(),
            new List<(string Name, string Content)>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("/my/programs/ExecutePlan", result);
        Assert.DoesNotContain("{PROGRAMFOLDER}", result);
    }

    [Fact]
    public void Compile_ReplacesLogFile()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            "/programs/Test/Logs/00042.md",
            new Dictionary<string, string>(),
            new List<(string Name, string Content)>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("/programs/Test/Logs/00042.md", result);
        Assert.DoesNotContain("{LOGFILE}", result);
    }

    [Fact]
    public void Compile_AppendsSharedDocuments()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            "/programs/Test/Logs/00001.md",
            new Dictionary<string, string>(),
            new List<(string Name, string Content)>
            {
                ("Plans", "# Plans\n\nPlan documentation here."),
                ("Config", "# Config\n\nConfig documentation here.")
            });

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("## Reference Documents", result);
        Assert.Contains("### Plans", result);
        Assert.Contains("Plan documentation here.", result);
        Assert.Contains("### Config", result);
        Assert.Contains("Config documentation here.", result);
    }

    [Fact]
    public void Compile_HeaderValuesSortedAlphabetically()
    {
        var context = new FirmwareContext(
            "/programs/Test",
            "/programs/Test/Logs/00001.md",
            new Dictionary<string, string> { ["Zebra"] = "last", ["Alpha"] = "first" },
            new List<(string Name, string Content)>());

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
            "/programs/Test/Logs/00001.md",
            new Dictionary<string, string>(),
            new List<(string Name, string Content)>());

        var result = FirmwareCompiler.Compile(context);

        Assert.Contains("You are an agentic application", result);
        Assert.Contains("Program.md", result);
        Assert.Contains("Reflection", result);
        Assert.Contains("Memory/", result);
        Assert.Contains("Tools/", result);
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
}
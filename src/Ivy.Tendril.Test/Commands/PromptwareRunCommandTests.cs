using Ivy.Tendril.Commands;
using Ivy.Tendril.Services.Agents;

namespace Ivy.Tendril.Test.Commands;

public class PromptwareRunCommandTests : IDisposable
{
    private readonly string _tempDir;

    public PromptwareRunCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"promptware-run-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void PromptwareRunSettings_ParsesPromptwareName()
    {
        var settings = new PromptwareRunSettings
        {
            Promptware = "IvyFrameworkVerification",
            Args = ["/plans/00123-TestPlan"]
        };

        Assert.Equal("IvyFrameworkVerification", settings.Promptware);
        Assert.Single(settings.Args);
        Assert.Equal("/plans/00123-TestPlan", settings.Args[0]);
    }

    [Fact]
    public void PromptwareRunSettings_DefaultsToEmptyArgs()
    {
        var settings = new PromptwareRunSettings { Promptware = "TestPromptware" };

        Assert.Empty(settings.Args);
        Assert.Null(settings.Profile);
        Assert.Null(settings.WorkingDir);
        Assert.Null(settings.Values);
    }

    [Fact]
    public void PromptwareRunSettings_SupportsProfile()
    {
        var settings = new PromptwareRunSettings
        {
            Promptware = "TestPromptware",
            Profile = "deep"
        };

        Assert.Equal("deep", settings.Profile);
    }

    [Fact]
    public void PromptwareRunSettings_SupportsValues()
    {
        var settings = new PromptwareRunSettings
        {
            Promptware = "TestPromptware",
            Values = ["VerificationDir=/tmp/v", "ArtifactsDir=/tmp/a"]
        };

        Assert.Equal(2, settings.Values.Length);
    }

    [Fact]
    public void FirmwareCompilation_WorksForArbitraryPromptware()
    {
        var promptwareDir = Path.Combine(_tempDir, "TestVerification");
        Directory.CreateDirectory(promptwareDir);
        File.WriteAllText(Path.Combine(promptwareDir, "Program.md"), "# TestVerification\n");

        var values = new Dictionary<string, string>
        {
            ["PlanFolder"] = "/plans/00123-Test",
            ["VerificationDir"] = "/plans/00123-Test/verification",
            ["ArtifactsDir"] = "/plans/00123-Test/artifacts"
        };

        var logFile = FirmwareCompiler.GetNextLogFile(promptwareDir);
        var context = new FirmwareContext(promptwareDir, logFile, values);
        var prompt = FirmwareCompiler.Compile(context);

        // Firmware references ProgramFolder for the agent to read at runtime
        Assert.Contains(promptwareDir, prompt);
        Assert.Contains("Program.md", prompt);
        // Header values are injected
        Assert.Contains("PlanFolder: /plans/00123-Test", prompt);
        Assert.Contains("VerificationDir: /plans/00123-Test/verification", prompt);
        Assert.Contains("ArtifactsDir: /plans/00123-Test/artifacts", prompt);
    }

    [Fact]
    public void FirmwareCompilation_InjectsCustomValues()
    {
        var promptwareDir = Path.Combine(_tempDir, "CustomValues");
        Directory.CreateDirectory(promptwareDir);
        File.WriteAllText(Path.Combine(promptwareDir, "Program.md"), "# Custom\n");

        var values = new Dictionary<string, string>
        {
            ["IvyFrameworkPath"] = "/repos/Ivy-Framework",
            ["CustomKey"] = "CustomValue"
        };

        var logFile = FirmwareCompiler.GetNextLogFile(promptwareDir);
        var context = new FirmwareContext(promptwareDir, logFile, values);
        var prompt = FirmwareCompiler.Compile(context);

        Assert.Contains("IvyFrameworkPath: /repos/Ivy-Framework", prompt);
        Assert.Contains("CustomKey: CustomValue", prompt);
    }
}
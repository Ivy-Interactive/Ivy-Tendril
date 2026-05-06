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

    // --- Settings Parsing ---

    [Fact]
    public void Settings_ParsesPromptwareName()
    {
        var settings = new PromptwareRunSettings
        {
            Promptware = "UpdateProject",
            Args = ["Setup verifications"]
        };

        Assert.Equal("UpdateProject", settings.Promptware);
        Assert.Single(settings.Args);
        Assert.Equal("Setup verifications", settings.Args[0]);
    }

    [Fact]
    public void Settings_DefaultsToEmptyArgs()
    {
        var settings = new PromptwareRunSettings { Promptware = "TestPromptware" };

        Assert.Empty(settings.Args);
        Assert.Null(settings.Profile);
        Assert.Null(settings.WorkingDir);
        Assert.Null(settings.Values);
        Assert.Null(settings.Plan);
        Assert.Null(settings.ConfigPath);
        Assert.Null(settings.AgentCmd);
        Assert.False(settings.DryRun);
    }

    [Fact]
    public void Settings_SupportsProfile()
    {
        var settings = new PromptwareRunSettings
        {
            Promptware = "TestPromptware",
            Profile = "deep"
        };

        Assert.Equal("deep", settings.Profile);
    }

    [Fact]
    public void Settings_SupportsValues()
    {
        var settings = new PromptwareRunSettings
        {
            Promptware = "TestPromptware",
            Values = ["ProjectName=MyProject", "Instructions=Do stuff"]
        };

        Assert.Equal(2, settings.Values.Length);
    }

    [Fact]
    public void Settings_SupportsPlan()
    {
        var settings = new PromptwareRunSettings
        {
            Promptware = "ExecutePlan",
            Plan = "03430"
        };

        Assert.Equal("03430", settings.Plan);
    }

    [Fact]
    public void Settings_SupportsTestingOptions()
    {
        var settings = new PromptwareRunSettings
        {
            Promptware = "TestPromptware",
            ConfigPath = "/tmp/test-config.yaml",
            AgentCmd = "echo",
            DryRun = true
        };

        Assert.Equal("/tmp/test-config.yaml", settings.ConfigPath);
        Assert.Equal("echo", settings.AgentCmd);
        Assert.True(settings.DryRun);
    }

    // --- Firmware Compilation ---

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

        Assert.Contains(promptwareDir, prompt);
        Assert.Contains("Program.md", prompt);
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
            ["ProjectName"] = "MyProject",
            ["Instructions"] = "Setup verifications"
        };

        var logFile = FirmwareCompiler.GetNextLogFile(promptwareDir);
        var context = new FirmwareContext(promptwareDir, logFile, values);
        var prompt = FirmwareCompiler.Compile(context);

        Assert.Contains("ProjectName: MyProject", prompt);
        Assert.Contains("Instructions: Setup verifications", prompt);
    }

    [Fact]
    public void FirmwareCompilation_FreeFormArgsJoinedIntoSingleValue()
    {
        var promptwareDir = Path.Combine(_tempDir, "FreeFormArgs");
        Directory.CreateDirectory(promptwareDir);
        File.WriteAllText(Path.Combine(promptwareDir, "Program.md"), "# FreeForm\n");

        var values = new Dictionary<string, string>
        {
            ["Args"] = "Setup verifications and review actions for this project."
        };

        var logFile = FirmwareCompiler.GetNextLogFile(promptwareDir);
        var context = new FirmwareContext(promptwareDir, logFile, values);
        var prompt = FirmwareCompiler.Compile(context);

        Assert.Contains("Args: Setup verifications and review actions for this project.", prompt);
    }

    // --- Plan Resolution ---

    [Fact]
    public void PlanOption_PopulatesPlanFirmwareValues()
    {
        // Create a plan folder structure
        var plansDir = Path.Combine(_tempDir, "Plans");
        var planFolder = Path.Combine(plansDir, "00123-TestPlan");
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), "state: Draft\ntitle: Test\n");

        var originalHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var originalPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS");
        try
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir);
            Environment.SetEnvironmentVariable("TENDRIL_PLANS", null);

            var folder = Ivy.Tendril.Helpers.PlanCommandHelpers.ResolvePlanFolder("00123");
            Assert.Contains("00123-TestPlan", folder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", originalHome);
            Environment.SetEnvironmentVariable("TENDRIL_PLANS", originalPlans);
        }
    }
}

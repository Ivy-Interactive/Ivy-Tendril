using Ivy.Tendril.Commands;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class VerificationCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-ver-cmd-test");
    private readonly string _originalTendrilHome;

    public VerificationCommandTests()
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

    // --- Add Verification Definition ---

    [Fact]
    public void AddVerification_CreatesDefinition()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "UnitTests", Prompt = "Run unit tests" });
        config.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Verifications);
        Assert.Equal("UnitTests", reloaded.Settings.Verifications[0].Name);
        Assert.Equal("Run unit tests", reloaded.Settings.Verifications[0].Prompt);
    }

    [Fact]
    public void AddVerification_MultipleDefinitions()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "UnitTests", Prompt = "Run units" });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "Lint", Prompt = "Run linter" });
        config.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal(2, reloaded.Settings.Verifications.Count);
        Assert.Equal("UnitTests", reloaded.Settings.Verifications[0].Name);
        Assert.Equal("Lint", reloaded.Settings.Verifications[1].Name);
    }

    // --- Remove Verification Definition ---

    [Fact]
    public void RemoveVerification_RemovesDefinition()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "ToRemove", Prompt = "x" });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "ToKeep", Prompt = "y" });
        config.SaveSettings();

        var config2 = CreateConfig();
        var match = config2.Settings.Verifications
            .First(v => v.Name.Equals("ToRemove", StringComparison.OrdinalIgnoreCase));
        config2.Settings.Verifications.Remove(match);
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Single(reloaded.Settings.Verifications);
        Assert.Equal("ToKeep", reloaded.Settings.Verifications[0].Name);
    }

    [Fact]
    public void RemoveVerification_LastEntry_LeavesEmptyList()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "Only", Prompt = "x" });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Verifications.RemoveAt(0);
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Empty(reloaded.Settings.Verifications);
    }

    // --- Set Verification Fields ---

    [Fact]
    public void SetVerification_UpdatesName()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "Original", Prompt = "test" });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Verifications[0].Name = "Renamed";
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal("Renamed", reloaded.Settings.Verifications[0].Name);
    }

    [Fact]
    public void SetVerification_UpdatesPrompt()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "Test", Prompt = "Old prompt" });
        config.SaveSettings();

        var config2 = CreateConfig();
        config2.Settings.Verifications[0].Prompt = "New prompt";
        config2.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal("New prompt", reloaded.Settings.Verifications[0].Prompt);
    }

    // --- Get Verification Definition ---

    [Fact]
    public void GetVerification_FindsByName_CaseInsensitive()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "Build", Prompt = "dotnet build --warnaserror" });
        config.SaveSettings();

        var reloaded = CreateConfig();
        var match = reloaded.Settings.Verifications
            .FirstOrDefault(v => v.Name.Equals("build", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(match);
        Assert.Equal("dotnet build --warnaserror", match.Prompt);
    }

    [Fact]
    public void GetVerification_ReturnsNull_WhenNotFound()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "Build", Prompt = "x" });
        config.SaveSettings();

        var reloaded = CreateConfig();
        var match = reloaded.Settings.Verifications
            .FirstOrDefault(v => v.Name.Equals("NonExistent", StringComparison.OrdinalIgnoreCase));

        Assert.Null(match);
    }

    // --- Get Verification: Not Found Error Lists Available ---

    private static CommandApp BuildVerificationGetApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddBranch("verification", verification =>
            {
                verification.AddCommand<VerificationGetCommand>("get");
            });
        });
        return app;
    }

    [Fact]
    public void GetVerification_NotFound_ListsAvailable()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "DotnetBuild", Prompt = "dotnet build" });
        config.SaveSettings();

        var app = BuildVerificationGetApp();
        var ex = Assert.Throws<InvalidOperationException>(() => app.Run(["verification", "get", "Build"]));

        Assert.Contains("Available: DotnetBuild", ex.Message);
    }

    [Fact]
    public void GetVerification_NotFound_EmptyList_ListsAvailable()
    {
        var app = BuildVerificationGetApp();
        var ex = Assert.Throws<InvalidOperationException>(() => app.Run(["verification", "get", "xyzzy"]));

        Assert.Contains("Available: ", ex.Message);
    }

    // --- Roundtrip ---

    [Fact]
    public void Verifications_SurviveRoundtrip()
    {
        var config = CreateConfig();
        config.Settings.Verifications.Add(new VerificationConfig { Name = "UnitTests", Prompt = "Run all unit tests and report" });
        config.Settings.Verifications.Add(new VerificationConfig { Name = "Build", Prompt = "Verify the project builds" });
        config.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal(2, reloaded.Settings.Verifications.Count);
        Assert.Equal("UnitTests", reloaded.Settings.Verifications[0].Name);
        Assert.Equal("Run all unit tests and report", reloaded.Settings.Verifications[0].Prompt);
        Assert.Equal("Build", reloaded.Settings.Verifications[1].Name);
        Assert.Equal("Verify the project builds", reloaded.Settings.Verifications[1].Prompt);
    }

    // --- Add Verification CLI (-p / --file / --stdin) ---

    private static CommandApp BuildVerificationAddApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddBranch("verification", verification => verification.AddCommand<VerificationAddCommand>("add"));
        });
        return app;
    }

    [Fact]
    public void VerificationAdd_InlinePrompt_AddsDefinition()
    {
        var app = BuildVerificationAddApp();

        var exit = app.Run(["verification", "add", "InlinePromptTest", "-p", "Inline prompt text"]);

        Assert.Equal(0, exit);
        var reloaded = CreateConfig();
        Assert.Equal("Inline prompt text", reloaded.Settings.Verifications.Single().Prompt);
    }

    [Fact]
    public void VerificationAdd_File_ReadsFromFile()
    {
        var file = Path.Combine(_tempDir.Path, "prompt.md");
        File.WriteAllText(file, "Prompt from file");
        var app = BuildVerificationAddApp();

        var exit = app.Run(["verification", "add", "FilePromptTest", "--file", file]);

        Assert.Equal(0, exit);
        var reloaded = CreateConfig();
        Assert.Equal("Prompt from file", reloaded.Settings.Verifications.Single().Prompt);
    }

    [Fact]
    public void VerificationAdd_Stdin_ReadsPipedInput()
    {
        var app = BuildVerificationAddApp();

        var originalIn = Console.In;
        Console.SetIn(new StringReader("Prompt from stdin"));
        try
        {
            var exit = app.Run(["verification", "add", "StdinPromptTest", "--stdin"]);
            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var reloaded = CreateConfig();
        Assert.Equal("Prompt from stdin", reloaded.Settings.Verifications.Single().Prompt);
    }

    [Fact]
    public void VerificationAdd_MultipleSources_FailsValidation()
    {
        Assert.False(new VerificationAddSettings { Name = "X", Prompt = "inline", FilePath = "f.md" }.Validate().Successful);

        var app = BuildVerificationAddApp();
        Assert.Throws<CommandRuntimeException>(() =>
            app.Run(["verification", "add", "MultiSourceTest", "-p", "inline", "--file", "f.md"]));
    }

    [Fact]
    public void VerificationAdd_NoSource_ThrowsAndNeverReadsStdin()
    {
        var app = BuildVerificationAddApp();

        var originalIn = Console.In;
        Console.SetIn(new StringReader("SENTINEL-SHOULD-NOT-BE-READ"));
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => app.Run(["verification", "add", "NoSourceTest"]));
            Assert.Contains("--prompt, --file, or --stdin", ex.Message);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var reloaded = CreateConfig();
        Assert.Empty(reloaded.Settings.Verifications);
    }

    // --- Concurrent writers (config.yaml is a whole-file read-modify-write) ---

    private static CommandApp BuildVerificationSetApp()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.PropagateExceptions();
            config.AddBranch("verification", verification => verification.AddCommand<VerificationSetCommand>("set"));
        });
        return app;
    }

    /// <summary>
    ///     Covers the command-level wiring, not just ConfigFileLock: `verification set` must not revert a
    ///     sibling definition that a separate ConfigService added after the command's own service loaded
    ///     the file. See ConfigFileLockTests for the helper-level coverage.
    /// </summary>
    [Fact]
    public void VerificationSet_PreservesSiblingAddedByAnotherWriter()
    {
        var seed = CreateConfig();
        seed.Settings.Verifications.Add(new VerificationConfig { Name = "Target", Prompt = "SEED-TARGET" });
        seed.SaveSettings();

        // Lands after the command's ConfigService would have loaded, before its write.
        var other = CreateConfig();
        other.MutateAndSave(s => s.Verifications.Add(new VerificationConfig { Name = "Sibling", Prompt = "SIBLING" }));

        var exit = BuildVerificationSetApp().Run(["verification", "set", "Target", "prompt", "UPDATED-TARGET"]);
        Assert.Equal(0, exit);

        var reloaded = CreateConfig();
        Assert.Equal(2, reloaded.Settings.Verifications.Count);
        Assert.Equal("UPDATED-TARGET", reloaded.Settings.Verifications.Single(v => v.Name == "Target").Prompt);
        Assert.Equal("SIBLING", reloaded.Settings.Verifications.Single(v => v.Name == "Sibling").Prompt);
    }
}

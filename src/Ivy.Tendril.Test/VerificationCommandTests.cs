using Ivy.Tendril.Services;

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
}

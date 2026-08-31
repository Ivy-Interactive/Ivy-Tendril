using Ivy.Tendril.Commands;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class ConfigCliCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-config-cmd-test");
    private readonly string _originalTendrilHome;

    public ConfigCliCommandTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), "projects: []\nverifications: []\n");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private static ConfigService CreateConfig() => new();

    // --- Round-trip: set persists and get reads it back ---

    [Fact]
    public void Set_JobTimeout_Persists()
    {
        var config = CreateConfig();
        ConfigSetCommand.ApplyField(config.Settings, "jobTimeout", "45");
        config.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal(45, reloaded.Settings.JobTimeout);
        Assert.Equal("45", ConfigGetCommand.ReadField(reloaded.Settings, "jobTimeout"));
    }

    [Fact]
    public void Set_CodingAgent_Persists()
    {
        var config = CreateConfig();
        ConfigSetCommand.ApplyField(config.Settings, "codingAgent", "codex");
        config.SaveSettings();

        Assert.Equal("codex", CreateConfig().Settings.CodingAgent);
    }

    [Fact]
    public void Set_CodingAgent_KnownAgent_StoresCanonicalId()
    {
        var agents = TestAgentRunner.Create().RegisteredAgents;
        var config = CreateConfig();
        // Mixed-case input resolves to the canonical registered id.
        ConfigSetCommand.ApplyField(config.Settings, "codingAgent", "Claude", agents);
        config.SaveSettings();

        Assert.Equal("claude", CreateConfig().Settings.CodingAgent);
    }

    [Fact]
    public void Set_CodingAgent_UnknownAgent_Throws()
    {
        var agents = TestAgentRunner.Create().RegisteredAgents;
        var ex = Assert.Throws<ArgumentException>(
            () => ConfigSetCommand.ApplyField(CreateConfig().Settings, "codingAgent", "notarealagent", agents));
        Assert.Contains("Valid agents", ex.Message);
    }

    [Theory]
    [InlineData("staleOutputTimeout", "20")]
    [InlineData("gitTimeout", "5")]
    [InlineData("maxConcurrentJobs", "8")]
    public void Set_IntFields_Persist(string key, string value)
    {
        var config = CreateConfig();
        ConfigSetCommand.ApplyField(config.Settings, key, value);
        config.SaveSettings();

        Assert.Equal(value, ConfigGetCommand.ReadField(CreateConfig().Settings, key));
    }

    [Fact]
    public void Set_PlanTemplate_MultilineValue_Persists()
    {
        var template = "# Plan\n\n- step [one]\n- step two with -flag\n";
        var config = CreateConfig();
        ConfigSetCommand.ApplyField(config.Settings, "planTemplate", template);
        config.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal(template, reloaded.Settings.PlanTemplate);
        Assert.Equal(template, ConfigGetCommand.ReadField(reloaded.Settings, "planTemplate"));
    }

    [Fact]
    public void Set_PlanTemplate_WithSlashesAndUrls_RoundTripsUnchanged()
    {
        // Prose template: forward slashes, a URL, and a // comment must survive verbatim
        // (previously ConfigService path-normalized this at load, mangling it).
        var template = "See src/Ivy.Tendril/Foo.cs and https://example.com/x\n// a comment";
        var config = CreateConfig();
        ConfigSetCommand.ApplyField(config.Settings, "planTemplate", template);
        config.SaveSettings();

        Assert.Equal(template, ConfigGetCommand.ReadField(CreateConfig().Settings, "planTemplate"));
    }

    [Fact]
    public void Set_UnrelatedField_DoesNotCorruptPlanTemplate()
    {
        var template = "paths like src/a/b and urls https://x.y/z\n// comment";
        var config = CreateConfig();
        ConfigSetCommand.ApplyField(config.Settings, "planTemplate", template);
        config.SaveSettings();

        // Setting an unrelated field reloads + re-saves the whole config; the template must survive.
        var config2 = CreateConfig();
        ConfigSetCommand.ApplyField(config2.Settings, "jobTimeout", "60");
        config2.SaveSettings();

        Assert.Equal(template, ConfigGetCommand.ReadField(CreateConfig().Settings, "planTemplate"));
    }

    // --- Bounds / parsing: rejected up front, before any write ---

    [Theory]
    [InlineData("jobTimeout", "999")]
    [InlineData("jobTimeout", "0")]
    [InlineData("gitTimeout", "31")]
    [InlineData("staleOutputTimeout", "0")]
    [InlineData("maxConcurrentJobs", "513")]
    public void Set_OutOfRange_Throws(string key, string value)
    {
        Assert.Throws<ArgumentException>(() => ConfigSetCommand.ApplyField(CreateConfig().Settings, key, value));
    }

    [Fact]
    public void Set_NonInteger_Throws()
    {
        Assert.Throws<ArgumentException>(() => ConfigSetCommand.ApplyField(CreateConfig().Settings, "jobTimeout", "soon"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Set_CodingAgent_Empty_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => ConfigSetCommand.ApplyField(CreateConfig().Settings, "codingAgent", value));
    }

    [Fact]
    public void Set_OutOfRange_DoesNotOverwriteExistingValue()
    {
        var config = CreateConfig();
        ConfigSetCommand.ApplyField(config.Settings, "jobTimeout", "45");
        config.SaveSettings();

        var config2 = CreateConfig();
        Assert.Throws<ArgumentException>(() => ConfigSetCommand.ApplyField(config2.Settings, "jobTimeout", "999"));

        // The bad value never reached disk.
        Assert.Equal(45, CreateConfig().Settings.JobTimeout);
    }

    // --- Unknown fields ---

    [Fact]
    public void Set_UnknownField_Throws()
    {
        Assert.Throws<ArgumentException>(() => ConfigSetCommand.ApplyField(CreateConfig().Settings, "bogus", "x"));
    }

    [Fact]
    public void Get_UnknownField_Throws()
    {
        Assert.Throws<ArgumentException>(() => ConfigGetCommand.ReadField(CreateConfig().Settings, "bogus"));
    }

    // --- Validation on the Spectre settings objects ---

    [Fact]
    public void GetSettings_Validate_RejectsUnknownKey()
    {
        Assert.False(new ConfigGetSettings { Key = "bogus" }.Validate().Successful);
        Assert.True(new ConfigGetSettings { Key = "jobTimeout" }.Validate().Successful);
    }

    [Fact]
    public void SetSettings_Validate_RejectsUnknownKey()
    {
        Assert.False(new ConfigSetSettings { Key = "bogus", Value = "1" }.Validate().Successful);
    }

    [Fact]
    public void SetSettings_Validate_RejectsMultipleValueSources()
    {
        Assert.False(new ConfigSetSettings { Key = "jobTimeout", Value = "1", Stdin = true }.Validate().Successful);
        Assert.False(new ConfigSetSettings { Key = "planTemplate", Value = "x", FilePath = "f.txt" }.Validate().Successful);
    }

    [Fact]
    public void SetSettings_Validate_AllowsSingleSource()
    {
        Assert.True(new ConfigSetSettings { Key = "jobTimeout", Value = "45" }.Validate().Successful);
        Assert.True(new ConfigSetSettings { Key = "planTemplate", FilePath = "f.txt" }.Validate().Successful);
        Assert.True(new ConfigSetSettings { Key = "planTemplate", Stdin = true }.Validate().Successful);
    }

    [Fact]
    public void Set_Theme_Persists()
    {
        var config = CreateConfig();
        ConfigSetCommand.ApplyField(config.Settings, "theme", "cupcake");
        config.SaveSettings();

        var reloaded = CreateConfig();
        Assert.Equal("cupcake", reloaded.Settings.Theme);
        Assert.Equal("cupcake", ConfigGetCommand.ReadField(reloaded.Settings, "theme"));
    }

    [Fact]
    public void Set_Theme_MixedCase_ResolvesCanonicalId()
    {
        var config = CreateConfig();
        ConfigSetCommand.ApplyField(config.Settings, "theme", "DrAcUlA");
        config.SaveSettings();

        Assert.Equal("dracula", CreateConfig().Settings.Theme);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Set_Theme_Empty_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => ConfigSetCommand.ApplyField(CreateConfig().Settings, "theme", value));
    }

    [Fact]
    public void Set_Theme_Unknown_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => ConfigSetCommand.ApplyField(CreateConfig().Settings, "theme", "nonexistent_theme"));
        Assert.Contains("Valid themes", ex.Message);
    }
}

using Ivy.Tendril.Commands;
using Ivy.Tendril.Mcp;
using Ivy.Tendril.Mcp.Tools;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Mcp;

[Collection("TendrilHome")]
public class ConfigToolsTests : IDisposable
{
    private readonly string _originalTendrilHome;
    private readonly string? _originalToken;
    private readonly string _tempDir;
    private readonly IConfigService _configService;
    private readonly ConfigTools _tools;

    public ConfigToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-config-mcp-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        _originalToken = Environment.GetEnvironmentVariable("TENDRIL_MCP_TOKEN");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", null);

        File.WriteAllText(Path.Combine(_tempDir, "config.yaml"), "projects: []\nverifications: []\n");
        _configService = new ConfigService();
        _tools = new ConfigTools(
            new McpAuthenticationService(NullLogger<McpAuthenticationService>.Instance),
            _configService,
            TestAgentRunner.Create());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        Environment.SetEnvironmentVariable("TENDRIL_MCP_TOKEN", _originalToken);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void SetConfig_UpdatesAndPersists()
    {
        var result = _tools.SetConfig("jobTimeout", "45");
        Assert.StartsWith("Updated jobTimeout", result);

        // Live singleton reflects it immediately...
        Assert.Equal("45", _tools.GetConfig("jobTimeout"));
        // ...and it was written to disk.
        Assert.Equal(45, new ConfigService().Settings.JobTimeout);
    }

    [Fact]
    public void SetConfig_PlanTemplate_MultilineRoundTrips()
    {
        var template = "# Plan\n\n- step [one]\n- step two with -flag\n";
        _tools.SetConfig("planTemplate", template);

        Assert.Equal(template, _tools.GetConfig("planTemplate"));
        Assert.Equal(template, new ConfigService().Settings.PlanTemplate);
    }

    [Fact]
    public void SetConfig_PlanTemplate_WithSlashesAndUrls_RoundTripsUnchanged()
    {
        var template = "See src/Ivy.Tendril/Foo.cs and https://example.com/x\n// a comment";
        _tools.SetConfig("planTemplate", template);

        Assert.Equal(template, _tools.GetConfig("planTemplate"));
        Assert.Equal(template, new ConfigService().Settings.PlanTemplate);
    }

    [Fact]
    public void SetConfig_CodingAgent_Empty_ReturnsError()
    {
        Assert.StartsWith("Error:", _tools.SetConfig("codingAgent", ""));
    }

    [Fact]
    public void SetConfig_CodingAgent_UnknownAgent_ReturnsError()
    {
        var result = _tools.SetConfig("codingAgent", "notarealagent");
        Assert.StartsWith("Error:", result);
        Assert.Contains("Valid agents", result);
    }

    [Fact]
    public void SetConfig_CodingAgent_KnownAgent_Persists()
    {
        Assert.StartsWith("Updated", _tools.SetConfig("codingAgent", "claude"));
        Assert.Equal("claude", _tools.GetConfig("codingAgent"));
    }

    [Fact]
    public void SetConfig_CodingAgent_MixedCase_ConfirmationReflectsCanonicalId()
    {
        // Input 'Claude' is stored (and reported) as the canonical 'claude'.
        Assert.Equal("Updated codingAgent to 'claude'", _tools.SetConfig("codingAgent", "Claude"));
        Assert.Equal("claude", _tools.GetConfig("codingAgent"));
    }

    [Fact]
    public void GetConfig_ReturnsCurrentValue()
    {
        _tools.SetConfig("codingAgent", "codex");
        Assert.Equal("codex", _tools.GetConfig("codingAgent"));
    }

    [Theory]
    [InlineData("jobTimeout", "999")]
    [InlineData("gitTimeout", "0")]
    [InlineData("maxConcurrentJobs", "101")]
    public void SetConfig_OutOfRange_ReturnsErrorAndKeepsPriorValue(string key, string value)
    {
        // Establish a known-good value first.
        _tools.SetConfig(key, "7");

        var result = _tools.SetConfig(key, value);
        Assert.StartsWith("Error:", result);

        // The rejected value never overwrote the good one, on disk or in memory.
        Assert.Equal("7", _tools.GetConfig(key));
        Assert.Equal("7", ConfigGetCommand.ReadField(new ConfigService().Settings, key));
    }

    [Fact]
    public void SetConfig_NonInteger_ReturnsError()
    {
        Assert.StartsWith("Error:", _tools.SetConfig("jobTimeout", "soon"));
    }

    [Fact]
    public void SetConfig_UnknownKey_ReturnsError()
    {
        var result = _tools.SetConfig("bogus", "x");
        Assert.StartsWith("Error:", result);
        Assert.Contains("Valid fields", result);
    }

    [Fact]
    public void GetConfig_UnknownKey_ReturnsError()
    {
        var result = _tools.GetConfig("bogus");
        Assert.StartsWith("Error:", result);
        Assert.Contains("Valid fields", result);
    }
}

using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Codex;

namespace Ivy.Tendril.Agents.Test.Codex;

public class CodexUsageProviderTests : IDisposable
{
    private readonly string _tempDir;

    public CodexUsageProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "codex_usage_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task ParsesPrimaryAndSecondary_IntoTwoWindows()
    {
        var now = DateTimeOffset.UtcNow;
        var file = Path.Combine(_tempDir, "rollout-1.jsonl");
        var json = """
        {"timestamp":"TIMESTAMP","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":14147}},"rate_limits":{"primary":{"used_percent":25.5,"window_minutes":300,"resets_at":1789391057},"secondary":{"used_percent":50.0,"window_minutes":10080,"resets_at":1789400000}}}}
        """.Replace("TIMESTAMP", now.ToString("O"));
        await File.WriteAllTextAsync(file, json + "\n");

        var provider = new CodexUsageProvider(_tempDir);
        var snapshot = await provider.GetUsageAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(AgentId.Codex, snapshot.AgentId);
        Assert.Equal(2, snapshot.Windows.Count);

        var primary = snapshot.Windows[0];
        Assert.Equal(300, primary.WindowMinutes);
        Assert.Equal(25.5, primary.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789391057), primary.ResetsAt);

        var secondary = snapshot.Windows[1];
        Assert.Equal(10080, secondary.WindowMinutes);
        Assert.Equal(50.0, secondary.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789400000), secondary.ResetsAt);
    }

    [Fact]
    public async Task UsesLastTokenCountLineInFile_NotFirst()
    {
        var now = DateTimeOffset.UtcNow;
        var file = Path.Combine(_tempDir, "rollout-last.jsonl");
        var line1 = """{"timestamp":"TIMESTAMP1","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":10.0,"window_minutes":300}}}}"""
            .Replace("TIMESTAMP1", now.AddMinutes(-5).ToString("O"));
        var line2 = """{"timestamp":"2026-09-09T12:00:00Z","type":"other_msg","payload":{"type":"other"}}""";
        var line3 = """{"timestamp":"TIMESTAMP3","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":80.0,"window_minutes":300}}}}"""
            .Replace("TIMESTAMP3", now.ToString("O"));

        await File.WriteAllTextAsync(file, line1 + "\n" + line2 + "\n" + line3 + "\n");

        var provider = new CodexUsageProvider(_tempDir);
        var snapshot = await provider.GetUsageAsync();

        Assert.NotNull(snapshot);
        Assert.Single(snapshot.Windows);
        Assert.Equal(80.0, snapshot.Windows[0].UsedPercent);
    }

    [Fact]
    public async Task ResetsAtAndResetsInSeconds_HandledCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var file = Path.Combine(_tempDir, "rollout-resets.jsonl");
        var line = """{"timestamp":"TIMESTAMP","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":10.0,"window_minutes":300,"resets_in_seconds":3600}}}}"""
            .Replace("TIMESTAMP", now.ToString("O"));
        await File.WriteAllTextAsync(file, line + "\n");

        var provider = new CodexUsageProvider(_tempDir);
        var snapshot = await provider.GetUsageAsync();

        Assert.NotNull(snapshot);
        Assert.Single(snapshot.Windows);
        var window = snapshot.Windows[0];
        Assert.NotNull(window.ResetsAt);
        var expectedResetsAt = now.AddSeconds(3600);
        Assert.True(Math.Abs((window.ResetsAt.Value - expectedResetsAt).TotalSeconds) < 2);
    }

    [Fact]
    public async Task SecondaryNull_YieldsExactlyOneWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var file = Path.Combine(_tempDir, "rollout-secondary-null.jsonl");
        var line = """{"timestamp":"TIMESTAMP","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5.0,"window_minutes":300},"secondary":null}}}"""
            .Replace("TIMESTAMP", now.ToString("O"));
        await File.WriteAllTextAsync(file, line + "\n");

        var provider = new CodexUsageProvider(_tempDir);
        var snapshot = await provider.GetUsageAsync();

        Assert.NotNull(snapshot);
        Assert.Single(snapshot.Windows);
        Assert.Equal(300, snapshot.Windows[0].WindowMinutes);
        Assert.Equal(5.0, snapshot.Windows[0].UsedPercent);
    }

    [Fact]
    public async Task SnapshotOlderThanWindow_ReturnsNull()
    {
        var oldTime = DateTimeOffset.UtcNow.AddHours(-6);
        var file = Path.Combine(_tempDir, "rollout-old.jsonl");
        var line = """{"timestamp":"TIMESTAMP","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5.0,"window_minutes":300}}}}"""
            .Replace("TIMESTAMP", oldTime.ToString("O"));
        await File.WriteAllTextAsync(file, line + "\n");

        var provider = new CodexUsageProvider(_tempDir);
        var snapshot = await provider.GetUsageAsync();

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task NoTokenCountLine_AndMalformedJson_BothReturnNull()
    {
        var file1 = Path.Combine(_tempDir, "no_token.jsonl");
        await File.WriteAllTextAsync(file1, "{\"type\":\"some_other_event\"}\n");

        var provider1 = new CodexUsageProvider(_tempDir);
        var snapshot1 = await provider1.GetUsageAsync();
        Assert.Null(snapshot1);

        var file2 = Path.Combine(_tempDir, "malformed.jsonl");
        await File.WriteAllTextAsync(file2, "{not valid json at all\n");

        var provider2 = new CodexUsageProvider(_tempDir);
        var snapshot2 = await provider2.GetUsageAsync();
        Assert.Null(snapshot2);
    }
}

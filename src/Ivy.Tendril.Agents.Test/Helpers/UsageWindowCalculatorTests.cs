using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Test.Helpers;

public class UsageWindowCalculatorTests : IDisposable
{
    private readonly string _tempDir;

    public UsageWindowCalculatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tendril_test_" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData(45, "45m")]
    [InlineData(300, "5h")]
    [InlineData(10080, "7d")]
    [InlineData(43200, "30d")]
    public void FormatWindow_ReturnsExpectedRepresentation(int minutes, string expected)
    {
        var result = UsageWindowCalculator.FormatWindow(minutes);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EnumerateRecentFiles_SkipsFilesPredatingSince_AndHonoursMaxFiles()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var fileOld = Path.Combine(_tempDir, "old.jsonl");
        var fileRecent1 = Path.Combine(_tempDir, "recent1.jsonl");
        var fileRecent2 = Path.Combine(_tempDir, "recent2.jsonl");
        var fileRecent3 = Path.Combine(_tempDir, "recent3.jsonl");

        File.WriteAllText(fileOld, "old");
        File.SetLastWriteTimeUtc(fileOld, baseTime.AddHours(-10).UtcDateTime);

        File.WriteAllText(fileRecent1, "recent1");
        File.SetLastWriteTimeUtc(fileRecent1, baseTime.AddHours(-2).UtcDateTime);

        File.WriteAllText(fileRecent2, "recent2");
        File.SetLastWriteTimeUtc(fileRecent2, baseTime.AddHours(-1).UtcDateTime);

        File.WriteAllText(fileRecent3, "recent3");
        File.SetLastWriteTimeUtc(fileRecent3, baseTime.UtcDateTime);

        var since = baseTime.AddHours(-5);
        var files = UsageWindowCalculator.EnumerateRecentFiles(_tempDir, since, maxFiles: 2);

        Assert.Equal(2, files.Count);
        Assert.Contains(fileRecent3, files);
        Assert.Contains(fileRecent2, files);
        Assert.DoesNotContain(fileOld, files);
        Assert.DoesNotContain(fileRecent1, files); // Excluded by maxFiles limit of 2 (newest 2 chosen)
    }

    [Theory]
    [InlineData(1_400_000, "1.4M")]
    [InlineData(812_000, "812k")]
    [InlineData(4321, "4321")]
    public void FormatTokens_ReturnsExpectedRepresentation(long tokens, string expected)
    {
        var result = UsageWindowCalculator.FormatTokens(tokens);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatCountdown_ReturnsExpectedRepresentation()
    {
        Assert.Equal("now", UsageWindowCalculator.FormatCountdown(TimeSpan.Zero));
        Assert.Equal("now", UsageWindowCalculator.FormatCountdown(TimeSpan.FromSeconds(-10)));
        Assert.Equal("2h 07m", UsageWindowCalculator.FormatCountdown(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(7)));
        Assert.Equal("43m", UsageWindowCalculator.FormatCountdown(TimeSpan.FromMinutes(43)));
    }
}

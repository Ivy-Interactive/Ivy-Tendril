using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceLogCostTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private string[] ReadCsv() => File.ReadAllLines(Path.Combine(_tempDir.Path, "costs.csv"));

    [Fact]
    public void LogCostToCsv_CreatesFileWithHeaders()
    {
        JobService.LogCostToCsv(_tempDir.Path, "ExecutePlan", 150000, 0.4500m, "claude-opus-5");

        var csvPath = Path.Combine(_tempDir.Path, "costs.csv");
        Assert.True(File.Exists(csvPath));

        var lines = File.ReadAllLines(csvPath);
        Assert.Equal("Promptware,Tokens,Cost,Model", lines[0]);
        Assert.Equal("ExecutePlan,150000,0.4500,claude-opus-5", lines[1]);
    }

    [Fact]
    public void LogCostToCsv_AppendsToExistingFile()
    {
        JobService.LogCostToCsv(_tempDir.Path, "ExecutePlan", 150000, 0.4500m, "claude-opus-5");
        JobService.LogCostToCsv(_tempDir.Path, "CreatePlan", 25000, 0.0750m, "claude-sonnet-5");

        var lines = ReadCsv();
        Assert.Equal(3, lines.Length);
        Assert.Equal("Promptware,Tokens,Cost,Model", lines[0]);
        Assert.Equal("ExecutePlan,150000,0.4500,claude-opus-5", lines[1]);
        Assert.Equal("CreatePlan,25000,0.0750,claude-sonnet-5", lines[2]);
    }

    [Fact]
    public void LogCostToCsv_SkipsNonexistentDirectory()
    {
        // Should not throw
        JobService.LogCostToCsv("/nonexistent/path/123", "Test", 100, 0.01m);
    }

    [Fact]
    public void LogCostToCsv_FormatsCorrectly()
    {
        JobService.LogCostToCsv(_tempDir.Path, "CreatePr", 99999, 1.23456789m);

        // Cost is written to 4 decimal places with the invariant culture, so the file parses the same
        // way on a machine whose decimal separator is a comma.
        Assert.Equal("CreatePr,99999,1.2346,", ReadCsv()[1]);
    }

    [Fact]
    public void LogCostToCsv_NullCost_WritesEmptyField()
    {
        JobService.LogCostToCsv(_tempDir.Path, "ExecutePlan", 150000, null, "claude-opus-5");

        // Not "0.0000": a subscription run that charged nothing observable is unknown, not free, and
        // the parser turns this empty field into a SQL NULL the aggregates skip.
        Assert.Equal("ExecutePlan,150000,,claude-opus-5", ReadCsv()[1]);
    }

    [Fact]
    public void LogCostToCsv_ZeroCost_WritesZeroDistinctFromNull()
    {
        JobService.LogCostToCsv(_tempDir.Path, "ExecutePlan", 150000, 0m, "claude-opus-5");

        Assert.Equal("ExecutePlan,150000,0.0000,claude-opus-5", ReadCsv()[1]);
    }

    [Fact]
    public void LogCostToCsv_NullModel_WritesEmptyFourthField()
    {
        JobService.LogCostToCsv(_tempDir.Path, "ExecutePlan", 150000, 0.4500m);

        var lines = ReadCsv();
        Assert.Equal("ExecutePlan,150000,0.4500,", lines[1]);
        Assert.Equal(4, lines[1].Split(',').Length);
    }

    [Fact]
    public void LogCostToCsv_ExistingThreeColumnFile_KeepsItsHeader()
    {
        var csvPath = Path.Combine(_tempDir.Path, "costs.csv");
        File.WriteAllText(csvPath, "Promptware,Tokens,Cost\nExecutePlan,50000,1.5000\n");

        JobService.LogCostToCsv(_tempDir.Path, "CreatePr", 99999, 1.2346m, "claude-opus-5");

        var lines = ReadCsv();
        // The header is left as it was found: rewriting it would mean rewriting the whole file, and
        // the parser reads by position anyway, so a short header over long rows is harmless.
        Assert.Equal("Promptware,Tokens,Cost", lines[0]);
        Assert.Equal("ExecutePlan,50000,1.5000", lines[1]);
        Assert.Equal("CreatePr,99999,1.2346,claude-opus-5", lines[2]);
    }
}

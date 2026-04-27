using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceLogCostTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }
    [Fact]
    public void LogCostToCsv_CreatesFileWithHeaders()
    {
        JobService.LogCostToCsv(_tempDir.Path, "ExecutePlan", 150000, 0.4500);

        var csvPath = Path.Combine(_tempDir.Path, "costs.csv");
            Assert.True(File.Exists(csvPath));

            var lines = File.ReadAllLines(csvPath);
            Assert.Equal("Promptware,Tokens,Cost", lines[0]);
            Assert.Equal("ExecutePlan,150000,0.4500", lines[1]);
    }

    [Fact]
    public void LogCostToCsv_AppendsToExistingFile()
    {
        JobService.LogCostToCsv(_tempDir.Path, "ExecutePlan", 150000, 0.4500);
            JobService.LogCostToCsv(_tempDir.Path, "CreatePlan", 25000, 0.0750);

        var csvPath = Path.Combine(_tempDir.Path, "costs.csv");
            var lines = File.ReadAllLines(csvPath);
            Assert.Equal(3, lines.Length);
            Assert.Equal("Promptware,Tokens,Cost", lines[0]);
            Assert.Equal("ExecutePlan,150000,0.4500", lines[1]);
            Assert.Equal("CreatePlan,25000,0.0750", lines[2]);
    }

    [Fact]
    public void LogCostToCsv_SkipsNonexistentDirectory()
    {
        // Should not throw
        JobService.LogCostToCsv("/nonexistent/path/123", "Test", 100, 0.01);
    }

    [Fact]
    public void LogCostToCsv_FormatsCorrectly()
    {
        JobService.LogCostToCsv(_tempDir.Path, "CreatePr", 99999, 1.23456789);

        var csvPath = Path.Combine(_tempDir.Path, "costs.csv");
            var lines = File.ReadAllLines(csvPath);
            // Cost should be formatted to 4 decimal places
            Assert.Equal("CreatePr,99999,1.2346", lines[1]);
    }
}
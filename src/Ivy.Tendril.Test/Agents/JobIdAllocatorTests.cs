using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Agents;

public class JobIdAllocatorTests : IDisposable
{
    private readonly string _tempDir;

    public JobIdAllocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"jobid-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { }
    }

    [Fact]
    public void AllocateJobId_ReturnsFirstId()
    {
        var id = JobIdAllocator.AllocateJobId(_tempDir);

        Assert.Equal("00001", id);
    }

    [Fact]
    public void AllocateJobId_IncrementsSequentially()
    {
        var first = JobIdAllocator.AllocateJobId(_tempDir);
        var second = JobIdAllocator.AllocateJobId(_tempDir);
        var third = JobIdAllocator.AllocateJobId(_tempDir);

        Assert.Equal("00001", first);
        Assert.Equal("00002", second);
        Assert.Equal("00003", third);
    }

    [Fact]
    public void AllocateJobId_CreatesCounterFile()
    {
        JobIdAllocator.AllocateJobId(_tempDir);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        Assert.True(File.Exists(counterFile));
    }

    [Fact]
    public void AllocateJobId_PersistsAcrossCalls()
    {
        JobIdAllocator.AllocateJobId(_tempDir);
        JobIdAllocator.AllocateJobId(_tempDir);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        var content = File.ReadAllText(counterFile).Trim();
        Assert.Equal("3", content);
    }

    [Fact]
    public void SeedIfNeeded_DoesNothingWhenCounterExists()
    {
        var jobsDir = Path.Combine(_tempDir, "Jobs");
        Directory.CreateDirectory(jobsDir);
        File.WriteAllText(Path.Combine(jobsDir, ".counter"), "50");

        var promptwaresRoot = Path.Combine(_tempDir, "Promptwares");
        var logsDir = Path.Combine(promptwaresRoot, "ExecutePlan", "Logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "00100.md"), "log");

        JobIdAllocator.SeedIfNeeded(_tempDir, promptwaresRoot);

        var content = File.ReadAllText(Path.Combine(jobsDir, ".counter")).Trim();
        Assert.Equal("50", content);
    }

    [Fact]
    public void SeedIfNeeded_SeedsFromExistingLogs()
    {
        var promptwaresRoot = Path.Combine(_tempDir, "Promptwares");
        var logsDir = Path.Combine(promptwaresRoot, "ExecutePlan", "Logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "00042.md"), "log");
        File.WriteAllText(Path.Combine(logsDir, "00010.md"), "log");

        JobIdAllocator.SeedIfNeeded(_tempDir, promptwaresRoot);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        var content = File.ReadAllText(counterFile).Trim();
        Assert.Equal("43", content);
    }

    [Fact]
    public void SeedIfNeeded_ScansMultiplePromptwareFolders()
    {
        var promptwaresRoot = Path.Combine(_tempDir, "Promptwares");
        var logs1 = Path.Combine(promptwaresRoot, "ExecutePlan", "Logs");
        var logs2 = Path.Combine(promptwaresRoot, "CreatePlan", "Logs");
        Directory.CreateDirectory(logs1);
        Directory.CreateDirectory(logs2);
        File.WriteAllText(Path.Combine(logs1, "00020.md"), "log");
        File.WriteAllText(Path.Combine(logs2, "00055.md"), "log");

        JobIdAllocator.SeedIfNeeded(_tempDir, promptwaresRoot);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        var content = File.ReadAllText(counterFile).Trim();
        Assert.Equal("56", content);
    }

    [Fact]
    public void SeedIfNeeded_IgnoresNonNumericFiles()
    {
        var promptwaresRoot = Path.Combine(_tempDir, "Promptwares");
        var logsDir = Path.Combine(promptwaresRoot, "ExecutePlan", "Logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "readme.md"), "not a log");
        File.WriteAllText(Path.Combine(logsDir, "00005.md"), "log");

        JobIdAllocator.SeedIfNeeded(_tempDir, promptwaresRoot);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        var content = File.ReadAllText(counterFile).Trim();
        Assert.Equal("6", content);
    }

    [Fact]
    public void SeedIfNeeded_NoOpWhenNoLogs()
    {
        var promptwaresRoot = Path.Combine(_tempDir, "Promptwares");
        Directory.CreateDirectory(promptwaresRoot);

        JobIdAllocator.SeedIfNeeded(_tempDir, promptwaresRoot);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        Assert.False(File.Exists(counterFile));
    }

    [Fact]
    public void AllocateJobId_StartsAfterSeed()
    {
        var promptwaresRoot = Path.Combine(_tempDir, "Promptwares");
        var logsDir = Path.Combine(promptwaresRoot, "ExecutePlan", "Logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "00090.md"), "log");

        JobIdAllocator.SeedIfNeeded(_tempDir, promptwaresRoot);
        var id = JobIdAllocator.AllocateJobId(_tempDir);

        Assert.Equal("00091", id);
    }

    [Fact]
    public void ScanMaxLogNumber_ReturnsZeroForEmptyDirectory()
    {
        var root = Path.Combine(_tempDir, "Empty");
        Directory.CreateDirectory(root);

        var max = JobIdAllocator.ScanMaxLogNumber(root);

        Assert.Equal(0, max);
    }

    [Fact]
    public void ScanMaxLogNumber_ReturnsZeroForNonExistentDirectory()
    {
        var max = JobIdAllocator.ScanMaxLogNumber(Path.Combine(_tempDir, "NoSuchDir"));

        Assert.Equal(0, max);
    }
}

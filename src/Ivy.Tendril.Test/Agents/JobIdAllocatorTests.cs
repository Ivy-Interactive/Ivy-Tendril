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

    /// <summary>Writes job logs into &lt;TendrilHome&gt;/Jobs/ using the real stem format.</summary>
    private void WriteJobLogs(params string[] stems)
    {
        var jobsDir = Path.Combine(_tempDir, "Jobs");
        Directory.CreateDirectory(jobsDir);
        foreach (var stem in stems)
            File.WriteAllText(Path.Combine(jobsDir, $"{stem}.md"), "log");
    }

    [Fact]
    public void SeedIfNeeded_DoesNothingWhenCounterExists()
    {
        var jobsDir = Path.Combine(_tempDir, "Jobs");
        Directory.CreateDirectory(jobsDir);
        File.WriteAllText(Path.Combine(jobsDir, ".counter"), "50");
        WriteJobLogs("00100-00007-ExecutePlan");

        JobIdAllocator.SeedIfNeeded(_tempDir);

        var content = File.ReadAllText(Path.Combine(jobsDir, ".counter")).Trim();
        Assert.Equal("50", content);
    }

    [Fact]
    public void SeedIfNeeded_SeedsFromExistingLogs()
    {
        WriteJobLogs("00042-00007-ExecutePlan", "00010-00003-ExecutePlan");

        JobIdAllocator.SeedIfNeeded(_tempDir);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        var content = File.ReadAllText(counterFile).Trim();
        Assert.Equal("43", content);
    }

    [Fact]
    public void SeedIfNeeded_ScansAcrossPromptwaresAndPlanlessJobs()
    {
        WriteJobLogs("00020-00007-ExecutePlan", "00055-CreatePlan");

        JobIdAllocator.SeedIfNeeded(_tempDir);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        var content = File.ReadAllText(counterFile).Trim();
        Assert.Equal("56", content);
    }

    [Fact]
    public void SeedIfNeeded_IgnoresNonNumericFiles()
    {
        WriteJobLogs("readme", "not-a-job-log", "00005-CreatePlan");

        JobIdAllocator.SeedIfNeeded(_tempDir);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        var content = File.ReadAllText(counterFile).Trim();
        Assert.Equal("6", content);
    }

    [Fact]
    public void SeedIfNeeded_NoOpWhenNoLogs()
    {
        JobIdAllocator.SeedIfNeeded(_tempDir);

        var counterFile = Path.Combine(_tempDir, "Jobs", ".counter");
        Assert.False(File.Exists(counterFile));
    }

    [Fact]
    public void AllocateJobId_StartsAfterSeed()
    {
        WriteJobLogs("00090-00007-ExecutePlan");

        JobIdAllocator.SeedIfNeeded(_tempDir);
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

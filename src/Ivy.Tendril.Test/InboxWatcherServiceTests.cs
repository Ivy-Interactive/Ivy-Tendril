using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class InboxWatcherServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }
    [Fact]
    public void ParseContent_PlainMarkdown_ReturnsAutoProject()
    {
        var content = "Add a new color picker widget with HSL support";

        var (project, description, sourcePath, _, _) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Auto", project);
        Assert.Equal(content, description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_WithFrontmatter_ExtractsProject()
    {
        var content = "---\nproject: Framework\n---\nAdd a new color picker widget";

        var (project, description, sourcePath, _, _) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Framework", project);
        Assert.Equal("Add a new color picker widget", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_FrontmatterWithoutProject_ReturnsAuto()
    {
        var content = "---\nlevel: Critical\n---\nFix the login bug";

        var (project, description, sourcePath, _, _) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Auto", project);
        Assert.Equal("Fix the login bug", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_EmptyDescriptionAfterFrontmatter_ReturnsEmpty()
    {
        var content = "---\nproject: Agent\n---\n";

        var (project, description, sourcePath, _, _) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Agent", project);
        Assert.Equal("", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_IncompleteYamlFrontmatter_TreatsAsPlainContent()
    {
        var content = "--- some header without closing";

        var (project, description, sourcePath, _, _) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Auto", project);
        Assert.Equal(content, description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_WithSourcePath_ExtractsAll()
    {
        var content = "---\nproject: Agent\nsourcePath: D:\\Tests\\Session123\n---\nFix the widget rendering";

        var (project, description, sourcePath, _, _) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Agent", project);
        Assert.Equal("Fix the widget rendering", description);
        Assert.Equal("D:\\Tests\\Session123", sourcePath);
    }

    [Fact]
    public void ParseContent_WithoutSourcePath_ReturnsNull()
    {
        var content = "---\nproject: Framework\n---\nAdd a button";

        var (project, description, sourcePath, _, _) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Framework", project);
        Assert.Equal("Add a button", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_EmptyContent_ReturnsEmptyDescription()
    {
        var (project, description, sourcePath, _, _) = InboxWatcherService.ParseContent("");

        Assert.Equal("Auto", project);
        Assert.Equal("", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_WhitespaceOnly_ReturnsWhitespaceDescription()
    {
        var (project, description, sourcePath, _, _) = InboxWatcherService.ParseContent("   \n  ");

        Assert.Equal("Auto", project);
        Assert.Equal("   \n  ", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public async Task ProcessFileAsync_EmptyContent_SkipsJob()
    {
        var inboxDir = Path.Combine(_tempDir.Path, "Inbox");
        Directory.CreateDirectory(inboxDir);

        // Place an empty file in the inbox
        var filePath = Path.Combine(inboxDir, "empty-entry.md");
        File.WriteAllText(filePath, "");

        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), inboxDir);
        using var watcher = new InboxWatcherService(config, jobService, NullLogger<InboxWatcherService>.Instance);

        // Wait for async processing to complete
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                await Task.Yield();
                return Directory.GetFiles(inboxDir, "*.md").Length == 1;
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(100),
            "Empty file was not skipped within timeout");

        // The .md file should still be there (not renamed to .processing) because the description is empty
        Assert.Single(Directory.GetFiles(inboxDir, "*.md"));
        Assert.Empty(Directory.GetFiles(inboxDir, "*.processing"));
    }

    [Fact]
    public async Task ProcessFileAsync_AlreadyTrackedByRunningJob_SkipsStartJob()
    {
        var inboxDir = Path.Combine(_tempDir.Path, "Inbox");
        Directory.CreateDirectory(inboxDir);

        var filePath = Path.Combine(inboxDir, "duplicate-entry.md");
        File.WriteAllText(filePath, "Fix the bug");

        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var jobService = new TrackedStubJobService { TrackedReturnValue = true };
        using var watcher = new InboxWatcherService(config, jobService, NullLogger<InboxWatcherService>.Instance);

        // Wait for processing to be attempted and skipped
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                await Task.Yield();
                // Processing should complete (file remains .md, no job started)
                return Directory.GetFiles(inboxDir, "*.md").Length == 1 &&
                       Directory.GetFiles(inboxDir, "*.md.processing").Length == 0;
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(100),
            "Tracked file was not skipped within timeout");

        // When IsInboxFileTracked returns true, the watcher must not call StartJob
        // and must leave the .md file untouched (no rename to .processing).
        Assert.Empty(jobService.StartedJobs);
        Assert.Single(Directory.GetFiles(inboxDir, "*.md"));
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md.processing"));
    }

    [Fact]
    public async Task ProcessFileAsync_NotTracked_StartsJobAndRenamesFile()
    {
        var inboxDir = Path.Combine(_tempDir.Path, "Inbox");
        Directory.CreateDirectory(inboxDir);

        var filePath = Path.Combine(inboxDir, "new-entry.md");
        File.WriteAllText(filePath, "Fix the bug");

        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var jobService = new TrackedStubJobService { TrackedReturnValue = false };
        using var watcher = new InboxWatcherService(config, jobService, NullLogger<InboxWatcherService>.Instance);

        // Wait for processing to complete (job started, file renamed)
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                await Task.Yield();
                return jobService.StartedJobs.Count == 1 &&
                       Directory.GetFiles(inboxDir, "*.md.processing").Length == 1;
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(100),
            "File was not processed within timeout");

        Assert.Single(jobService.StartedJobs);
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md"));
        Assert.Single(Directory.GetFiles(inboxDir, "*.md.processing"));
    }

    private class TrackedStubJobService : IJobService
    {
        public bool TrackedReturnValue { get; set; }
        public List<(JobArgsBase Args, string? InboxFilePath)> StartedJobs { get; } = new();

        public string StartJob(JobArgsBase args, string? inboxFilePath = null)
        {
            StartedJobs.Add((args, inboxFilePath));
            return $"job-{StartedJobs.Count:D3}";
        }

        public void ForceStartJob(string id) { }

        public bool IsInboxFileTracked(string filePath) => TrackedReturnValue;

        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public List<JobItem> GetJobs() => new();
        public List<JobItem> GetJobsForPlan(string planFile) => new();
        public JobItem? GetJob(string id) => null;
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => false;
        public void Dispose() { }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }

    [Fact]
    public async Task ProcessExistingFiles_PicksUpFilesInInbox()
    {
        var inboxDir = Path.Combine(_tempDir.Path, "Inbox");
        Directory.CreateDirectory(inboxDir);

        // Place a file in the inbox before creating the service
        File.WriteAllText(Path.Combine(inboxDir, "test-entry.md"), "Test inbox entry");

        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), inboxDir);
        using var watcher = new InboxWatcherService(config, jobService, NullLogger<InboxWatcherService>.Instance);

        // The constructor calls ProcessExistingFiles, which dispatches async processing.
        // Wait for the file to be processed and renamed to .processing.
        await RetryHelper.WaitUntilAsync(
            async () =>
            {
                await Task.Yield();
                return Directory.GetFiles(inboxDir, "*.md").Length == 0 &&
                       Directory.GetFiles(inboxDir, "*.md.processing").Length == 1;
            },
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(100),
            "Existing file was not processed within timeout");

        // The .md file should have been renamed to .processing (job started)
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md"));
    }

    [Fact]
    public void ProcessFileAsync_FileDeletedBeforeRename_SkipsGracefully()
    {
        var inboxDir = Path.Combine(_tempDir.Path, "Inbox");
        Directory.CreateDirectory(inboxDir);

        // Create a file, then immediately delete it to simulate a race condition
        var filePath = Path.Combine(inboxDir, "race-condition-test.md");
        File.WriteAllText(filePath, "Test content for race condition");

        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var jobService = new DeleteBeforeRenameJobService(filePath);
        using var watcher = new InboxWatcherService(config, jobService, NullLogger<InboxWatcherService>.Instance);

        // The fake deletes the file during the watcher's tracked-check; once it's gone, processing
        // has reached (and handled) the race. Poll for that instead of guessing a fixed delay.
        RetryHelper.WaitUntil(() => !File.Exists(filePath), TimeSpan.FromSeconds(10));

        // No job should have been started, and no .processing file should exist
        Assert.Empty(jobService.StartedJobs);
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md"));
        Assert.Empty(Directory.GetFiles(inboxDir, "*.processing"));
    }

    [Fact]
    public void ProcessExistingFiles_StaggersStartupWithDelay()
    {
        var inboxDir = Path.Combine(_tempDir.Path, "Inbox");
        Directory.CreateDirectory(inboxDir);

        // Create 5 inbox files with staggered creation times
        var files = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var filePath = Path.Combine(inboxDir, $"entry-{i}.md");
            File.WriteAllText(filePath, $"Test entry {i}");
            Thread.Sleep(100); // Small delay to ensure different creation times
            files.Add(filePath);
        }

        var config = new ConfigService(new TendrilSettings(), _tempDir.Path);
        var jobService = new TimestampedJobService();

        // The constructor runs ProcessExistingFiles, which launches the 5 files with a 2s
        // Thread.Sleep between each — so construction alone blocks ~8s.
        var constructionStart = DateTime.UtcNow;
        using var watcher = new InboxWatcherService(config, jobService, NullLogger<InboxWatcherService>.Instance);

        // Wait for all jobs to be started
        RetryHelper.WaitUntil(() => jobService.StartedJobs.Count >= 5, TimeSpan.FromSeconds(30));

        // Verify we got all 5 jobs
        Assert.Equal(5, jobService.StartedJobs.Count);

        // Verify files were processed in creation order (oldest first)
        for (int i = 0; i < 5; i++)
        {
            var processingPath = files[i] + ".processing";
            Assert.Contains(jobService.StartedJobs, job => job.InboxFilePath == processingPath);
        }

        // Verify the startup is staggered rather than a thundering herd. Per-gap timing is not a
        // reliable signal: each StartJob is recorded after a fire-and-forget Task.Delay(500) whose
        // continuation can be scheduled with arbitrary jitter under a loaded thread pool. What IS
        // deterministic is the cumulative floor — the 4 × Thread.Sleep(2000) between launches is a
        // wall-clock minimum (load can only lengthen it), so the 5 jobs must span at least ~8s.
        var span = (jobService.StartJobTimestamps.Last() - constructionStart).TotalMilliseconds;
        Assert.True(span >= 7000,
            $"Expected staggered startup to span >= ~8s, but the 5 jobs started within {span}ms");
    }

    private class DeleteBeforeRenameJobService : IJobService
    {
        private readonly string _fileToDelete;
        public List<(JobArgsBase Args, string? InboxFilePath)> StartedJobs { get; } = new();

        public DeleteBeforeRenameJobService(string fileToDelete)
        {
            _fileToDelete = fileToDelete;
        }

        public string StartJob(JobArgsBase args, string? inboxFilePath = null)
        {
            StartedJobs.Add((args, inboxFilePath));
            return $"job-{StartedJobs.Count:D3}";
        }

        public void ForceStartJob(string id) { }

        public bool IsInboxFileTracked(string filePath)
        {
            // Delete the file when InboxWatcherService checks if it's tracked
            // This simulates the race condition between the existence check and File.Move
            if (File.Exists(_fileToDelete))
            {
                File.Delete(_fileToDelete);
            }
            return false;
        }

        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public List<JobItem> GetJobs() => new();
        public List<JobItem> GetJobsForPlan(string planFile) => new();
        public JobItem? GetJob(string id) => null;
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => false;
        public void Dispose() { }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }

    private class TimestampedJobService : IJobService
    {
        public List<(JobArgsBase Args, string? InboxFilePath)> StartedJobs { get; } = new();
        public List<DateTime> StartJobTimestamps { get; } = new();

        public string StartJob(JobArgsBase args, string? inboxFilePath = null)
        {
            StartJobTimestamps.Add(DateTime.UtcNow);
            StartedJobs.Add((args, inboxFilePath));
            return $"job-{StartedJobs.Count:D3}";
        }

        public void ForceStartJob(string id) { }

        public bool IsInboxFileTracked(string filePath) => false;

        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public List<JobItem> GetJobs() => new();
        public List<JobItem> GetJobsForPlan(string planFile) => new();
        public JobItem? GetJob(string id) => null;
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => false;
        public void Dispose() { }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }
}

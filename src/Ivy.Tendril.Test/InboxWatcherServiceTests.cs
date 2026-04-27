using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class InboxWatcherServiceTests
{
    [Fact]
    public void ParseContent_PlainMarkdown_ReturnsAutoProject()
    {
        var content = "Add a new color picker widget with HSL support";

        var (project, description, sourcePath) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Auto", project);
        Assert.Equal(content, description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_WithFrontmatter_ExtractsProject()
    {
        var content = "---\nproject: Framework\n---\nAdd a new color picker widget";

        var (project, description, sourcePath) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Framework", project);
        Assert.Equal("Add a new color picker widget", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_FrontmatterWithoutProject_ReturnsAuto()
    {
        var content = "---\nlevel: Critical\n---\nFix the login bug";

        var (project, description, sourcePath) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Auto", project);
        Assert.Equal("Fix the login bug", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_EmptyDescriptionAfterFrontmatter_ReturnsEmpty()
    {
        var content = "---\nproject: Agent\n---\n";

        var (project, description, sourcePath) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Agent", project);
        Assert.Equal("", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_IncompleteYamlFrontmatter_TreatsAsPlainContent()
    {
        var content = "--- some header without closing";

        var (project, description, sourcePath) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Auto", project);
        Assert.Equal(content, description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_WithSourcePath_ExtractsAll()
    {
        var content = "---\nproject: Agent\nsourcePath: D:\\Tests\\Session123\n---\nFix the widget rendering";

        var (project, description, sourcePath) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Agent", project);
        Assert.Equal("Fix the widget rendering", description);
        Assert.Equal("D:\\Tests\\Session123", sourcePath);
    }

    [Fact]
    public void ParseContent_WithoutSourcePath_ReturnsNull()
    {
        var content = "---\nproject: Framework\n---\nAdd a button";

        var (project, description, sourcePath) = InboxWatcherService.ParseContent(content);

        Assert.Equal("Framework", project);
        Assert.Equal("Add a button", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_EmptyContent_ReturnsEmptyDescription()
    {
        var (project, description, sourcePath) = InboxWatcherService.ParseContent("");

        Assert.Equal("Auto", project);
        Assert.Equal("", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public void ParseContent_WhitespaceOnly_ReturnsWhitespaceDescription()
    {
        var (project, description, sourcePath) = InboxWatcherService.ParseContent("   \n  ");

        Assert.Equal("Auto", project);
        Assert.Equal("   \n  ", description);
        Assert.Null(sourcePath);
    }

    [Fact]
    public async Task ProcessFileAsync_EmptyContent_SkipsJob()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"inbox-test-{Guid.NewGuid():N}");
        var inboxDir = Path.Combine(tempDir, "Inbox");
        Directory.CreateDirectory(inboxDir);

        try
        {
            // Place an empty file in the inbox
            var filePath = Path.Combine(inboxDir, "empty-entry.md");
            File.WriteAllText(filePath, "");

            var config = new ConfigService(new TendrilSettings(), tempDir);
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
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ProcessFileAsync_AlreadyTrackedByRunningJob_SkipsStartJob()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"inbox-test-{Guid.NewGuid():N}");
        var inboxDir = Path.Combine(tempDir, "Inbox");
        Directory.CreateDirectory(inboxDir);

        try
        {
            var filePath = Path.Combine(inboxDir, "duplicate-entry.md");
            File.WriteAllText(filePath, "Fix the bug");

            var config = new ConfigService(new TendrilSettings(), tempDir);
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
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ProcessFileAsync_NotTracked_StartsJobAndRenamesFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"inbox-test-{Guid.NewGuid():N}");
        var inboxDir = Path.Combine(tempDir, "Inbox");
        Directory.CreateDirectory(inboxDir);

        try
        {
            var filePath = Path.Combine(inboxDir, "new-entry.md");
            File.WriteAllText(filePath, "Fix the bug");

            var config = new ConfigService(new TendrilSettings(), tempDir);
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
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private class TrackedStubJobService : IJobService
    {
        public bool TrackedReturnValue { get; set; }
        public List<(string Type, string[] Args, string? InboxFilePath)> StartedJobs { get; } = new();

        public string StartJob(string type, string[] args, string? inboxFilePath)
        {
            StartedJobs.Add((type, args, inboxFilePath));
            return $"job-{StartedJobs.Count:D3}";
        }

        public string StartJob(string type, params string[] args) => StartJob(type, args, null);

        public bool IsInboxFileTracked(string filePath) => TrackedReturnValue;

        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public List<JobItem> GetJobs() => new();
        public JobItem? GetJob(string id) => null;
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"inbox-test-{Guid.NewGuid():N}");
        var inboxDir = Path.Combine(tempDir, "Inbox");
        Directory.CreateDirectory(inboxDir);

        try
        {
            // Place a file in the inbox before creating the service
            File.WriteAllText(Path.Combine(inboxDir, "test-entry.md"), "Test inbox entry");

            var config = new ConfigService(new TendrilSettings(), tempDir);
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
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

/// <summary>
///     Covers the breadcrumb lifecycle behind #2710: 39 inbox tasks became 99 CreatePlan jobs in one
///     hour because every start resurrected every breadcrumb, and every resurrected job wrote a new
///     breadcrumb of its own. The tests here pin down which submissions write a breadcrumb, which
///     breadcrumbs come back, and how often.
/// </summary>
public class InboxBreadcrumbRecoveryTests
{
    // 2A: only an inbox submission owns a breadcrumb

    [Theory]
    [InlineData(JobOrigin.Cli)]
    [InlineData(JobOrigin.Chat)]
    [InlineData(JobOrigin.Api)]
    [InlineData(JobOrigin.Ui)]
    [InlineData(JobOrigin.Unspecified)]
    public void ChatOrCliCreatePlan_WritesNoBreadcrumb(JobOrigin origin)
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), inboxDir);

        var id = jobService.CreateTestJob(new CreatePlanArgs("Add a widget", "Tendril", Origin: origin));

        var job = jobService.GetJob(id);
        Assert.NotNull(job);
        Assert.Null(job.InboxFile);
        Assert.Empty(Directory.GetFiles(inboxDir));
    }

    [Fact]
    public void InboxCreatePlan_WritesBreadcrumbNamedByTaskHash()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), inboxDir);

        var id = jobService.CreateTestJob(
            new CreatePlanArgs("Add a widget", "Tendril", Origin: JobOrigin.Inbox));

        var job = jobService.GetJob(id);
        Assert.NotNull(job?.InboxFile);
        Assert.Equal(
            Path.Combine(inboxDir, InboxBreadcrumb.FileName("Tendril", "Add a widget")),
            job.InboxFile);
        Assert.True(File.Exists(job.InboxFile));
    }

    [Fact]
    public void SameProjectAndDescription_ShareOneBreadcrumbFileName()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), inboxDir);

        // The same request spelled two ways. Naming breadcrumbs by job id used to give these two files,
        // so a restart resurrected the task twice.
        var first = jobService.CreateTestJob(
            new CreatePlanArgs("  Fix   The Bug  ", "proj", Origin: JobOrigin.Inbox));
        var second = jobService.CreateTestJob(
            new CreatePlanArgs("fix the bug", "proj", Origin: JobOrigin.Inbox));

        Assert.Single(Directory.GetFiles(inboxDir, "*.md.processing"));

        var firstJob = jobService.GetJob(first);
        var secondJob = jobService.GetJob(second);
        Assert.NotNull(firstJob?.InboxFile);
        Assert.Equal(firstJob.InboxFile, secondJob?.InboxFile);
    }

    [Fact]
    public void FileName_NormalisesWhitespaceAndCaseButNotProject()
    {
        Assert.Equal(
            InboxBreadcrumb.FileName("proj", "fix the bug"),
            InboxBreadcrumb.FileName("proj", "  Fix   The\tBug  "));

        Assert.Equal(InboxBreadcrumb.FileName("proj", "x"), InboxBreadcrumb.FileName("PROJ", "x"));
        Assert.NotEqual(InboxBreadcrumb.FileName("proj", "x"), InboxBreadcrumb.FileName("other", "x"));
        Assert.EndsWith(".md.processing", InboxBreadcrumb.FileName("proj", "x"));
    }

    // 2B: the breadcrumb pointer outlives the process

    [Fact]
    public void LoadHistoricalJobs_RestoresInboxFileAndChatSessionId()
    {
        using var temp = new TempDirectoryFixture();
        var db = new PlanDatabaseService(
            Path.Combine(temp.Path, "tendril.db"), NullLogger<PlanDatabaseService>.Instance);

        try
        {
            var inboxFile = Path.Combine(temp.Path, "Inbox", "pending-0123456789abcdef.md.processing");
            db.UpsertJob(new JobItem
            {
                Id = "job-hist-001",
                Type = "CreatePlan",
                Project = "Tendril",
                Status = JobStatus.Completed,
                InboxFile = inboxFile,
                TypedArgs = new CreatePlanArgs("Historical task", "Tendril", Origin: JobOrigin.Inbox),
                ChatSessionId = "chat-hist"
            });

            // A fresh process: recovery can only decide anything if the pointer came back from the DB.
            var service = new JobService(
                TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), database: db);

            var job = service.GetJobs().Single(j => j.Id == "job-hist-001");
            Assert.Equal(inboxFile, job.InboxFile);
            Assert.Equal("chat-hist", job.ChatSessionId);
        }
        finally
        {
            db.Dispose();
            SqliteConnection.ClearAllPools();
        }
    }

    [Fact]
    public void GetJobByInboxFile_FindsTheOwningJob()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), inboxDir);

        var id = jobService.CreateTestJob(
            new CreatePlanArgs("Owned task", "Tendril", Origin: JobOrigin.Inbox));
        var breadcrumb = jobService.GetJob(id)!.InboxFile!;

        Assert.Equal(id, jobService.GetJobByInboxFile(breadcrumb)?.Id);
        Assert.Null(jobService.GetJobByInboxFile(Path.Combine(inboxDir, "pending-nobody.md.processing")));
    }

    // 2C: recovery is a decision per file, not a blanket rename

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Stopped)]
    [InlineData(JobStatus.Timeout)]
    public async Task Recovery_TerminalJob_DeletesBreadcrumbAndDoesNotEnqueue(JobStatus status)
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var breadcrumb = WriteBreadcrumb(inboxDir, "terminal", "Already finished");

        var jobService = new CountingJobService();
        jobService.Register(breadcrumb, status);

        using var watcher = NewWatcher(temp, jobService);
        watcher.Start();
        await watcher.ProcessExistingFilesAsync();

        Assert.False(File.Exists(breadcrumb));
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md"));
        Assert.Empty(jobService.StartedJobs);

        var summary = watcher.LastRecovery;
        Assert.NotNull(summary);
        Assert.Equal(1, summary.Deleted);
        Assert.Equal(0, summary.Resurrected);
    }

    /// <summary>
    ///     A breadcrumb whose job really was interrupted comes back, but not forever: the counter it
    ///     carries is what turns an endless resubmission loop into two attempts and a dead letter.
    /// </summary>
    [Fact]
    public void Recovery_RunningJob_ResurrectsExactlyOnceThenDeadLetters()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var breadcrumb = WriteBreadcrumb(inboxDir, "running", "Interrupted task");
        var md = breadcrumb[..^".processing".Length];

        var jobService = new CountingJobService();
        jobService.Register(breadcrumb, JobStatus.Running);

        using var watcher = NewWatcher(temp, jobService);

        var first = watcher.RecoverProcessingFiles();
        Assert.Equal(1, first.Resurrected);
        Assert.True(File.Exists(md));
        Assert.Equal(1, InboxBreadcrumb.ReadRecoveryAttempts(File.ReadAllText(md)));

        // The watcher picked it up and the job was interrupted again.
        File.Move(md, breadcrumb);
        var second = watcher.RecoverProcessingFiles();
        Assert.Equal(1, second.Resurrected);
        Assert.Equal(2, InboxBreadcrumb.ReadRecoveryAttempts(File.ReadAllText(md)));

        File.Move(md, breadcrumb);
        var third = watcher.RecoverProcessingFiles();
        Assert.Equal(0, third.Resurrected);
        Assert.Equal(1, third.DeadLettered);
        Assert.False(File.Exists(md));
        Assert.False(File.Exists(breadcrumb));
        Assert.True(File.Exists(Path.Combine(inboxDir, "DeadLetter", Path.GetFileName(breadcrumb))));
    }

    [Fact]
    public void Recovery_OrphanBreadcrumb_ResurrectedOnce()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var breadcrumb = WriteBreadcrumb(inboxDir, "orphan", "Nobody owns me");

        using var watcher = NewWatcher(temp, new CountingJobService());

        var summary = watcher.RecoverProcessingFiles();

        Assert.Equal(1, summary.Orphaned);
        Assert.Equal(0, summary.Deleted);
        var md = breadcrumb[..^".processing".Length];
        Assert.True(File.Exists(md));
        Assert.Equal(1, InboxBreadcrumb.ReadRecoveryAttempts(File.ReadAllText(md)));
    }

    [Fact]
    public void Recovery_AttemptCapExceeded_MovesToDeadLetter()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var breadcrumb = Path.Combine(inboxDir, "pending-capped.md.processing");
        File.WriteAllText(breadcrumb, InboxBreadcrumb.WithRecoveryAttempts(
            "---\nproject: Tendril\n---\nTried too often", InboxBreadcrumb.RecoveryAttemptCap));

        var jobService = new CountingJobService();
        jobService.Register(breadcrumb, JobStatus.Running);

        var entries = new List<(LogLevel Level, string Message)>();
        using var watcher = NewWatcher(temp, jobService, new CapturingLogger<InboxWatcherService>(entries));

        var summary = watcher.RecoverProcessingFiles();

        Assert.Equal(1, summary.DeadLettered);
        Assert.Equal(0, summary.Resurrected);
        Assert.False(File.Exists(breadcrumb));
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md"));
        Assert.True(File.Exists(Path.Combine(inboxDir, "DeadLetter", "pending-capped.md.processing")));

        // A file that stops being retried has to say so somewhere, or it is just lost.
        Assert.Contains(entries, e => e.Level == LogLevel.Warning && e.Message.Contains("dead-letter"));
    }

    [Fact]
    public void Recovery_AboveBulkThreshold_RefusesMassResurrection()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var jobService = new CountingJobService();
        for (var i = 0; i < InboxWatcherService.BulkResurrectionLimit + 1; i++)
            jobService.Register(WriteBreadcrumb(inboxDir, $"bulk-{i}", $"Task {i}"), JobStatus.Running);

        var entries = new List<(LogLevel Level, string Message)>();
        using var watcher = NewWatcher(temp, jobService, new CapturingLogger<InboxWatcherService>(entries));

        var summary = watcher.RecoverProcessingFiles();

        // A pass that wants to enqueue this many jobs is describing a defect, so it enqueues none and
        // leaves the files where a human can look at them.
        Assert.True(summary.BulkRefused);
        Assert.Equal(0, summary.Resurrected);
        Assert.Equal(0, summary.Orphaned);
        Assert.Equal(
            InboxWatcherService.BulkResurrectionLimit + 1,
            Directory.GetFiles(inboxDir, "*.md.processing").Length);
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md"));
        Assert.Contains(entries, e => e.Level == LogLevel.Warning && e.Message.Contains("bulk-refused"));
    }

    [Fact]
    public void Recovery_AtBulkThreshold_ResurrectsEverything()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var jobService = new CountingJobService();
        for (var i = 0; i < InboxWatcherService.BulkResurrectionLimit; i++)
            jobService.Register(WriteBreadcrumb(inboxDir, $"bulk-{i}", $"Task {i}"), JobStatus.Running);

        using var watcher = NewWatcher(temp, jobService);

        var summary = watcher.RecoverProcessingFiles();

        Assert.False(summary.BulkRefused);
        Assert.Equal(InboxWatcherService.BulkResurrectionLimit, summary.Resurrected);
        Assert.Equal(InboxWatcherService.BulkResurrectionLimit, Directory.GetFiles(inboxDir, "*.md").Length);
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md.processing"));
    }

    // 2D: an orderly exit leaves no breadcrumb behind

    [Fact]
    public void Dispose_ResolvesInFlightBreadcrumbs()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), inboxDir);

        var id = jobService.CreateTestJob(
            new CreatePlanArgs("Still running at shutdown", "Tendril", Origin: JobOrigin.Inbox));
        var breadcrumb = jobService.GetJob(id)!.InboxFile!;
        Assert.True(File.Exists(breadcrumb));

        // Without this, the next start cannot tell an orderly exit from a crash and resubmits the job.
        jobService.Dispose();

        Assert.False(File.Exists(breadcrumb));
    }

    [Fact]
    public void ResolveInFlightInboxBreadcrumbs_IsIdempotent()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var jobService = new JobService(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), inboxDir);

        jobService.CreateTestJob(
            new CreatePlanArgs("Still running at shutdown", "Tendril", Origin: JobOrigin.Inbox));

        // Both ApplicationStopping and Dispose call this, and one exit runs both.
        Assert.Equal(1, jobService.ResolveInFlightInboxBreadcrumbs());
        Assert.Equal(0, jobService.ResolveInFlightInboxBreadcrumbs());
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md.processing"));
    }

    // The storm shape from #2710, in miniature

    [Fact]
    public async Task Startup_WithManyTerminalBreadcrumbs_CreatesZeroJobs()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var jobService = new CountingJobService();

        var terminal = new[] { JobStatus.Completed, JobStatus.Failed, JobStatus.Stopped, JobStatus.Timeout };
        for (var i = 0; i < 20; i++)
            jobService.Register(WriteBreadcrumb(inboxDir, $"storm-{i}", $"Task {i}"), terminal[i % terminal.Length]);

        using var watcher = NewWatcher(temp, jobService);
        watcher.Start();
        await watcher.ProcessExistingFilesAsync();

        // This used to be 20 new jobs, each writing a breadcrumb of its own for the next start to find.
        Assert.Empty(jobService.StartedJobs);
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md"));
        Assert.Empty(Directory.GetFiles(inboxDir, "*.md.processing"));
        Assert.Equal(20, watcher.LastRecovery?.Deleted);
    }

    [Fact]
    public void RecoveryCompleted_FiresOncePerPassAndSetsLastRecovery()
    {
        using var temp = new TempDirectoryFixture();
        var inboxDir = CreateInbox(temp);
        var breadcrumb = WriteBreadcrumb(inboxDir, "notified", "Nobody owns me");

        using var watcher = NewWatcher(temp, new CountingJobService());

        var seen = new List<InboxRecoverySummary>();
        watcher.RecoveryCompleted += seen.Add;

        var summary = watcher.RecoverProcessingFiles();

        Assert.Equal(summary, Assert.Single(seen));
        Assert.Equal(summary, watcher.LastRecovery);
        Assert.False(summary.IsEmpty);
        Assert.False(File.Exists(breadcrumb));
    }

    [Fact]
    public void EmptyInbox_PublishesAnEmptySummary()
    {
        using var temp = new TempDirectoryFixture();
        CreateInbox(temp);
        using var watcher = NewWatcher(temp, new CountingJobService());

        Assert.True(watcher.RecoverProcessingFiles().IsEmpty);
    }

    // Frontmatter round-trips

    [Fact]
    public void WithRecoveryAttempts_PreservesOtherFrontmatterKeys()
    {
        var content = "---\nproject: Tendril\nsourcePath: /issues/2710\n---\nFix the storm";

        var stamped = InboxBreadcrumb.WithRecoveryAttempts(content, 1);

        Assert.Equal(1, InboxBreadcrumb.ReadRecoveryAttempts(stamped));
        var (project, description, sourcePath) = InboxWatcherService.ParseContent(stamped);
        Assert.Equal("Tendril", project);
        Assert.Equal("Fix the storm", description);
        Assert.Equal("/issues/2710", sourcePath);
    }

    [Fact]
    public void WithRecoveryAttempts_RewritesRatherThanAppendsOnSecondPass()
    {
        var once = InboxBreadcrumb.WithRecoveryAttempts("---\nproject: Tendril\n---\nBody", 1);
        var twice = InboxBreadcrumb.WithRecoveryAttempts(once, 2);

        Assert.Equal(2, InboxBreadcrumb.ReadRecoveryAttempts(twice));
        Assert.Equal(1, twice.Split('\n').Count(l => l.TrimStart().StartsWith("recoveryAttempts:")));
    }

    [Fact]
    public void WithRecoveryAttempts_AddsFrontmatterWhenThereIsNone()
    {
        var stamped = InboxBreadcrumb.WithRecoveryAttempts("Just a description", 1);

        Assert.Equal(1, InboxBreadcrumb.ReadRecoveryAttempts(stamped));
        var (_, description, _) = InboxWatcherService.ParseContent(stamped);
        Assert.Equal("Just a description", description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No frontmatter here")]
    [InlineData("---\nproject: Tendril\n---\nBody")]
    [InlineData("---\nrecoveryAttempts: not-a-number\n---\nBody")]
    [InlineData("---\nrecoveryAttempts:\n---\nBody")]
    [InlineData("---\nrecoveryAttempts: -3\n---\nBody")]
    public void ReadRecoveryAttempts_TreatsAnythingUnreadableAsZero(string? content)
    {
        // An unreadable counter must not look like an exhausted one: that would dead-letter real work.
        Assert.Equal(0, InboxBreadcrumb.ReadRecoveryAttempts(content));
    }

    private static string CreateInbox(TempDirectoryFixture temp)
    {
        var inboxDir = Path.Combine(temp.Path, "Inbox");
        Directory.CreateDirectory(inboxDir);
        return inboxDir;
    }

    private static string WriteBreadcrumb(string inboxDir, string name, string description)
    {
        var path = Path.Combine(inboxDir, $"pending-{name}.md.processing");
        File.WriteAllText(path, $"---\nproject: Tendril\n---\n{description}");
        return path;
    }

    private static InboxWatcherService NewWatcher(
        TempDirectoryFixture temp, IJobService jobService, ILogger<InboxWatcherService>? logger = null)
        => new(
            new ConfigService(new TendrilSettings(), temp.Path),
            jobService,
            logger ?? NullLogger<InboxWatcherService>.Instance);

    /// <summary>
    ///     Counts <see cref="StartJob" /> calls and answers <see cref="GetJobByInboxFile" /> from a
    ///     registry, so a test can put a breadcrumb's owning job in any status without a database.
    /// </summary>
    private sealed class CountingJobService : IJobService
    {
        private readonly Dictionary<string, JobItem> _byInboxFile = new();

        public List<(JobArgsBase Args, string? InboxFilePath)> StartedJobs { get; } = new();

        public void Register(string inboxFile, JobStatus status)
        {
            _byInboxFile[inboxFile] = new JobItem
            {
                Id = $"job-{_byInboxFile.Count + 1:D3}",
                Type = "CreatePlan",
                Project = "Tendril",
                Status = status,
                InboxFile = inboxFile
            };
        }

        public JobItem? GetJobByInboxFile(string filePath) =>
            _byInboxFile.GetValueOrDefault(filePath);

        public string StartJob(JobArgsBase args, string? inboxFilePath = null)
        {
            StartedJobs.Add((args, inboxFilePath));
            return $"job-started-{StartedJobs.Count:D3}";
        }

        public bool IsInboxFileTracked(string filePath) => false;

        public void ForceStartJob(string id) { }
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public int StopAllJobs() => 0;
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public int StopQueuedJobs() => 0;
        public List<JobItem> GetJobs() => _byInboxFile.Values.ToList();
        public List<JobItem> GetJobsForPlan(string planFile) => new();
        public JobItem? GetJob(string id) => _byInboxFile.Values.FirstOrDefault(j => j.Id == id);
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => false;
        public bool ReportJobFailure(string id, string message) => false;
        public void SetChatSessionId(string id, string chatSessionId) { }
        public void Dispose() { }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobItem>? JobFinished;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }
}

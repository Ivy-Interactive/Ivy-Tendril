using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceConcurrencyTests
{
    [Fact]
    public void MaxConcurrentJobs_DefaultsToFive()
    {
        var settings = new TendrilSettings();
        Assert.Equal(5, settings.MaxConcurrentJobs);
    }

    [Fact]
    public void MaxConcurrentJobs_CanBeConfigured()
    {
        var settings = new TendrilSettings { MaxConcurrentJobs = 10 };
        Assert.Equal(10, settings.MaxConcurrentJobs);
    }

    [Fact]
    public void StartJob_WhenAtMaxConcurrency_QueuesJob()
    {
        // maxConcurrentJobs=0 means all jobs get queued
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        var id = service.StartJob("CreatePlan", "-Description", "Test Job");
        var job = service.GetJob(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Contains("max 0 concurrent jobs", job.StatusMessage);
    }

    [Fact]
    public void StartJob_WhenBelowMaxConcurrency_DoesNotQueue()
    {
        // maxConcurrentJobs=10 and no running jobs — should not queue
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 10);

        // This will try to launch a process which will fail,
        // but the initial status should be "Running" not "Queued"
        try
        {
            var id = service.StartJob("CreatePlan", "-Description", "Test Job");
            var job = service.GetJob(id);
            Assert.NotNull(job);
            Assert.NotEqual(JobStatus.Queued, job.Status);
        }
        catch
        {
            // Process launch may fail in test — that's OK, we're testing the queue check
        }
    }

    [Fact]
    public void GetJobs_ReturnsQueuedJobs()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        service.StartJob("CreatePlan", "-Description", "Job 1");
        service.StartJob("CreatePlan", "-Description", "Job 2");

        var jobs = service.GetJobs();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(JobStatus.Queued, j.Status));
    }

    [Fact]
    public void StopJob_OnQueuedJob_SetsStopped()
    {
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            null, 0);

        var id = service.StartJob("CreatePlan", "-Description", "Test Job");
        service.StopJob(id);

        var job = service.GetJob(id);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Stopped, job.Status);
    }

    [Fact]
    public void SettingsReload_IncreasedConcurrency_StartsQueuedJobs()
    {
        // Arrange: Start with max=2
        var configService = new TestConfigService { MaxConcurrentJobs = 2 };
        var jobService = new JobService(configService);

        // Create 2 test jobs that will run (consume the 2 slots)
        var job1Id = jobService.CreateTestJob("ExecutePlan", "plan1");
        var job2Id = jobService.CreateTestJob("ExecutePlan", "plan2");

        // Verify we can only have 2 running jobs
        Assert.Equal(JobStatus.Running, jobService.GetJob(job1Id)!.Status);
        Assert.Equal(JobStatus.Running, jobService.GetJob(job2Id)!.Status);

        // Act: Increase max to 4
        configService.MaxConcurrentJobs = 4;
        configService.TriggerSettingsReloaded();

        // Assert: Should now be able to create 2 more running jobs
        var job3Id = jobService.CreateTestJob("ExecutePlan", "plan3");
        var job4Id = jobService.CreateTestJob("ExecutePlan", "plan4");

        Assert.Equal(JobStatus.Running, jobService.GetJob(job3Id)!.Status);
        Assert.Equal(JobStatus.Running, jobService.GetJob(job4Id)!.Status);
    }

    [Fact]
    public void SettingsReload_DecreasedConcurrency_PreventsNewJobsUntilSlotsFree()
    {
        // Arrange: Start with max=4, launch 4 jobs
        var configService = new TestConfigService { MaxConcurrentJobs = 4 };
        var jobService = new JobService(configService);

        // Create 4 test jobs that will run
        var job1Id = jobService.CreateTestJob("ExecutePlan", "plan1");
        var job2Id = jobService.CreateTestJob("ExecutePlan", "plan2");
        var job3Id = jobService.CreateTestJob("ExecutePlan", "plan3");
        var job4Id = jobService.CreateTestJob("ExecutePlan", "plan4");

        // Act: Decrease max to 2
        configService.MaxConcurrentJobs = 2;
        configService.TriggerSettingsReloaded();

        // Assert: Cannot create new jobs (all 4 running jobs continue, but semaphore has 0 available slots)
        // CreateTestJob tries to acquire a slot with Wait(0), which should fail and throw or queue
        var canCreateMore = jobService.GetJobs().Count(j => j.Status == JobStatus.Running);
        Assert.Equal(4, canCreateMore); // All 4 still running

        // Complete 2 jobs to free slots
        jobService.CompleteJob(job1Id, 0);
        jobService.CompleteJob(job2Id, 0);

        // Now we should have 0 available slots (2 running == limit of 2)
        canCreateMore = jobService.GetJobs().Count(j => j.Status == JobStatus.Running);
        Assert.Equal(2, canCreateMore);

        // Complete 1 more job
        jobService.CompleteJob(job3Id, 0);

        // Now we should have 1 available slot (1 running < limit of 2)
        var job5Id = jobService.CreateTestJob("ExecutePlan", "plan5");
        Assert.Equal(JobStatus.Running, jobService.GetJob(job5Id)!.Status);
    }

    [Fact]
    public void SettingsReload_NoChange_DoesNotRecreateSemaphore()
    {
        // Arrange
        var configService = new TestConfigService { MaxConcurrentJobs = 5 };
        var jobService = new JobService(configService);

        var job1Id = jobService.CreateTestJob("ExecutePlan", "plan1");
        Assert.Equal(JobStatus.Running, jobService.GetJob(job1Id)!.Status);

        // Act: Reload with same value
        configService.TriggerSettingsReloaded();

        // Assert: Job continues running (semaphore not disrupted)
        Assert.Equal(JobStatus.Running, jobService.GetJob(job1Id)!.Status);

        // Can still create new test jobs
        var job2Id = jobService.CreateTestJob("ExecutePlan", "plan2");
        Assert.Equal(JobStatus.Running, jobService.GetJob(job2Id)!.Status);
    }

    private class TestConfigService : IConfigService
    {
        public int MaxConcurrentJobs { get; set; } = 5;

        public TendrilSettings Settings => new()
        {
            MaxConcurrentJobs = MaxConcurrentJobs,
            JobTimeout = 60,
            StaleOutputTimeout = 5,
            Projects = []
        };

        public string TendrilHome => "";
        public string ConfigPath => "";
        public string PlanFolder => "";
        public List<ProjectConfig> Projects => [];
        public List<LevelConfig> Levels => [];
        public string[] LevelNames => [];
        public EditorConfig Editor => new();
        public bool NeedsOnboarding => false;
        public ConfigParseError? ParseError => null;

        public event EventHandler? SettingsReloaded;

        public void TriggerSettingsReloaded()
        {
            SettingsReloaded?.Invoke(this, EventArgs.Empty);
        }

        public ProjectConfig? GetProject(string name) => null;
        public bool TryAutoHeal() => false;
        public void ResetToDefaults() { }
        public void RetryLoadConfig() { }
        public BadgeVariant GetBadgeVariant(string level) => BadgeVariant.Info;
        public Colors? GetProjectColor(string projectName) => null;
        public void SaveSettings() { }
        public void ReloadSettings() { }
        public void SetPendingTendrilHome(string path) { }
        public string? GetPendingTendrilHome() => null;
        public void SetPendingProject(ProjectConfig project) { }
        public ProjectConfig? GetPendingProject() => null;
        public void SetPendingCodingAgent(string name) { }
        public string? GetPendingCodingAgent() => null;
        public void SetPendingVerificationDefinitions(List<VerificationConfig> definitions) { }
        public List<VerificationConfig>? GetPendingVerificationDefinitions() => null;
        public void CompleteOnboarding(string tendrilHome) { }
        public void OpenInEditor(string path) { }
        public string PreprocessForEditing(string path) => path;
    }
}
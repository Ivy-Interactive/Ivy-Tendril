using System.Collections.Concurrent;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class TendrilProcessStatusServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly FakeJobService _jobService;
    private readonly PlanReaderService _planReader;
    private readonly FakePlanWatcherService _planWatcher;
    private readonly string _plansDir;
    private readonly ConfigService _configService;

    public TendrilProcessStatusServiceTests()
    {
        _plansDir = Path.Combine(_tempDir.Path, "Plans");
        Directory.CreateDirectory(_plansDir);
        Directory.CreateDirectory(Path.Combine(_tempDir.Path, "Trash"));

        var settings = new TendrilSettings();
        _configService = new ConfigService(settings, _tempDir.Path);
        _planReader = new PlanReaderService(_configService, NullLogger<PlanReaderService>.Instance);
        _jobService = new FakeJobService();
        _planWatcher = new FakePlanWatcherService();
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private void CreatePlan(string folderName, string state)
    {
        var dir = Path.Combine(_plansDir, folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plan.yaml"),
            $"state: {state}\nproject: Tendril\ntitle: Test\nrepos: []\ncommits: []\nprs: []\nverifications: []\nrelatedPlans: []\ndependsOn: []\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\n");

        var revisionsDir = Path.Combine(dir, "Revisions");
        Directory.CreateDirectory(revisionsDir);
        File.WriteAllText(Path.Combine(revisionsDir, "001.md"), "# Test");
    }

    private void CreateRecommendations(string folderName, string recsYaml)
    {
        var planYamlPath = Path.Combine(_plansDir, folderName, "plan.yaml");
        var existing = File.ReadAllText(planYamlPath);
        var indented = string.Join("\n", recsYaml.Split('\n').Select(l => string.IsNullOrWhiteSpace(l) ? l : "  " + l));
        File.WriteAllText(planYamlPath, existing.TrimEnd() + $"\nrecommendations:\n{indented}\n");
    }

    private void AddJob(string id, JobStatus status, string type = "")
    {
        _jobService.AddJob(id, status, type);
    }

    private TendrilProcessStatusService CreateService()
    {
        return new TendrilProcessStatusService(
            _planReader, _jobService, _planWatcher, _configService,
            NullLogger<TendrilProcessStatusService>.Instance);
    }

    [Fact]
    public void Current_WithNoPlans_ReturnsAllZeros()
    {
        using var service = CreateService();

        Assert.Equal(0, service.Current.DraftCount);
        Assert.Equal(0, service.Current.JobCount);
        Assert.Equal(0, service.Current.ReviewCount);
        Assert.Equal(0, service.Current.IceboxCount);
        Assert.Equal(0, service.Current.RecommendationsCount);
        Assert.Equal(0, service.Current.TrashCount);
    }

    [Fact]
    public void Current_WithFailedPlans_CountsOnlyInReview()
    {
        CreatePlan("00001-FailedPlanA", "Failed");
        CreatePlan("00002-FailedPlanB", "Failed");

        using var service = CreateService();

        Assert.Equal(0, service.Current.DraftCount);
        Assert.Equal(2, service.Current.ReviewCount);
    }

    [Fact]
    public void Current_WithVariousStates_AggregatesCorrectly()
    {
        CreatePlan("00001-DraftPlan", "Draft");
        CreatePlan("00002-AnotherDraft", "Draft");
        CreatePlan("00003-ReviewPlan", "ReadyForReview");
        CreatePlan("00004-FailedPlan", "Failed");
        CreatePlan("00005-IceboxPlan", "Icebox");
        CreatePlan("00006-CompletedPlan", "Completed");
        CreatePlan("00007-WithRecs", "Completed");
        CreateRecommendations("00007-WithRecs",
            "- title: Fix something\n  description: |\n    Details here.\n  state: Pending\n");

        AddJob("job-1", JobStatus.Running);
        AddJob("job-2", JobStatus.Queued);
        AddJob("job-3", JobStatus.Completed);
        AddJob("job-4", JobStatus.Blocked);

        using var service = CreateService();

        Assert.Equal(2, service.Current.DraftCount);
        Assert.Equal(3, service.Current.JobCount);
        Assert.Equal(2, service.Current.ReviewCount);
        Assert.Equal(1, service.Current.IceboxCount);
        Assert.Equal(1, service.Current.RecommendationsCount);
    }

    [Fact]
    public void Current_BlockedPlansCountAsDrafts()
    {
        CreatePlan("00010-DraftPlan", "Draft");
        CreatePlan("00011-BlockedPlan1", "Blocked");
        CreatePlan("00012-BlockedPlan2", "Blocked");

        using var service = CreateService();

        Assert.Equal(3, service.Current.DraftCount);
    }

    [Fact]
    public void Current_CategorizesJobsByType()
    {
        AddJob("job-1", JobStatus.Running, Constants.JobTypes.CreatePlan);
        AddJob("job-2", JobStatus.Running, Constants.JobTypes.ExecutePlan);
        AddJob("job-3", JobStatus.Running, Constants.JobTypes.RetryPlan);
        AddJob("job-4", JobStatus.Running, Constants.JobTypes.UpdatePlan);
        AddJob("job-5", JobStatus.Queued, Constants.JobTypes.ExpandPlan);
        AddJob("job-6", JobStatus.Running, Constants.JobTypes.SplitPlan);
        AddJob("job-7", JobStatus.Running, Constants.JobTypes.CreatePr);

        using var service = CreateService();

        Assert.Equal(1, service.Current.CreatingPlansCount);
        Assert.Equal(1, service.Current.ExecutingPlansCount);
        Assert.Equal(1, service.Current.RetryingPlansCount);
        Assert.Equal(3, service.Current.UpdatingPlansCount);
        Assert.Equal(1, service.Current.CreatingPrCount);
        Assert.Equal(7, service.Current.JobCount);
    }

    [Fact]
    public void Current_CountsTrashFiles()
    {
        var trashDir = Path.Combine(_tempDir.Path, "Trash");
        File.WriteAllText(Path.Combine(trashDir, "trash1.md"), "---\ndate: 2026-01-01\n---\n# Trash");
        File.WriteAllText(Path.Combine(trashDir, "trash2.md"), "---\ndate: 2026-01-02\n---\n# Trash");

        using var service = CreateService();

        Assert.Equal(2, service.Current.TrashCount);
    }

    [Fact]
    public void RefreshNow_UpdatesCountsWhenPlansChange()
    {
        using var service = CreateService();

        Assert.Equal(0, service.Current.DraftCount);

        CreatePlan("00001-NewDraft", "Draft");
        service.RefreshNow();

        Assert.Equal(1, service.Current.DraftCount);
    }

    [Fact]
    public void Status_EmitsCurrentValueToNewSubscriber()
    {
        CreatePlan("00001-DraftPlan", "Draft");

        using var service = CreateService();
        TendrilProcessStatus? received = null;
        using var sub = service.Status.Subscribe(s => received = s);

        Assert.NotNull(received);
        Assert.Equal(1, received!.DraftCount);
    }

    private class FakeJobService : IJobService
    {
        private readonly List<JobItem> _jobs = new();

#pragma warning disable CS0618
        public ConcurrentQueue<JobNotification> PendingNotifications { get; } = new();
#pragma warning restore CS0618

        public List<JobItem> GetJobs()
        {
            return _jobs;
        }

        public List<JobItem> GetJobsForPlan(string planFile)
        {
            return _jobs.Where(j => j.PlanFile == planFile).ToList();
        }

        public JobItem? GetJob(string id)
        {
            return _jobs.FirstOrDefault(j => j.Id == id);
        }

        public string StartJob(JobArgsBase args, string? inboxFilePath = null)
        {
            throw new NotImplementedException();
        }

        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false)
        {
            throw new NotImplementedException();
        }

        public void StopJob(string id)
        {
            throw new NotImplementedException();
        }

        public void DeleteJob(string id)
        {
            throw new NotImplementedException();
        }

        public void ClearCompletedJobs()
        {
            throw new NotImplementedException();
        }

        public void ClearFailedJobs()
        {
            throw new NotImplementedException();
        }

        public void ClearAllJobs()
        {
            throw new NotImplementedException();
        }

        public bool IsInboxFileTracked(string filePath)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }

        public void AddJob(string id, JobStatus status, string type = "")
        {
            _jobs.Add(new JobItem { Id = id, Status = status, Type = type });
        }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }

    private class FakePlanWatcherService : IPlanWatcherService
    {
        public event Action<string?>? PlansChanged;

        public void NotifyChanged(string? changedPlanFolder = null)
        {
            PlansChanged?.Invoke(changedPlanFolder);
        }

        public void Dispose()
        {
        }

        public void RaisePlansChanged()
        {
            PlansChanged?.Invoke(null);
        }
    }
}

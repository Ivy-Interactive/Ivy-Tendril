using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test;

public class WorkflowTriggerServiceTests
{
    private class FakeDatabase : IPlanDatabaseService
    {
        public List<WorkflowItem> WorkflowsList { get; set; } = new();
        public Dictionary<string, string> PrStatuses { get; set; } = new();

        public List<WorkflowItem> GetWorkflows(string? project = null) => WorkflowsList;
        public Dictionary<string, string> GetAllPrStatuses() => PrStatuses;

        public void Dispose() { }
        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null) => new();
        public PlanFile? GetPlanByFolder(string folderPath) => null;
        public PlanFile? GetPlanById(int planId) => null;
        public PlanReaderService.PlanCountSnapshot ComputePlanCounts() => new(0, 0, 0, 0, 0, 0);
        public DashboardModels GetDashboardData(string? projectFilter) => new DashboardModels(0, 0, 0, 0, 0, 0, 0, new(), new());
        public List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days = 30) => new();
        public decimal GetPlanTotalCost(int planId) => 0;
        public int GetPlanTotalTokens(int planId) => 0;
        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null) => new();
        public List<Recommendation> GetRecommendations() => new();
        public int GetPendingRecommendationsCount() => 0;
        public List<PlanFile> SearchPlans(string query) => new();
        public void RebuildFtsIndex() { }
        public void UpdatePlanState(int planId, PlanStatus state) { }
        public void UpdatePlanContent(int planId, string latestRevisionContent, int revisionCount) { }
        public void UpdateRecommendationState(int planId, string recommendationTitle, string newState, string? declineReason) { }
        public void UpsertPlan(PlanFile plan) { }
        public void DeletePlan(int planId) { }
        public void UpsertCosts(int planId, List<CostEntry> costs) { }
        public void UpsertRecommendations(int planId, string folderName, List<RecommendationYaml> recommendations, string project, string planTitle, DateTime updated, PlanStatus status) { }
        public void BulkUpsertPlans(List<PlanFile> plans, bool forceOverwrite = false) { }
        public HashSet<int> GetTerminalPlanIds() => new();
        public void UpsertJob(JobItem job) { }
        public List<JobItem> GetRecentJobs(int limit = 100) => new();
        public JobItem? GetJobById(string id) => null;
        public List<JobItem> GetJobsForPlan(string planFile) => new();
        public List<string> PurgeOldJobs(int keepCount = 500) => new();
        public void DeleteJob(string id) { }
        public void UpsertPrStatus(string prUrl, string owner, string repo, string status, DateTime lastChecked) { }
        public List<string> GetNonMergedPrUrls() => new();
        public void UpsertConnection(ConnectionItem connection) { }
        public List<ConnectionItem> GetConnections() => new();
        public ConnectionItem? GetConnectionByName(string name) => null;
        public void DeleteConnection(string name) { }
        public void UpsertWorkflow(WorkflowItem workflow) { }
        public WorkflowItem? GetWorkflowById(int id) => null;
        public WorkflowItem? GetWorkflowByName(string name, string? project = null) => null;
        public void DeleteWorkflow(int id) { }
        public long GetDatabaseSize() => 0;
        public DateTime GetLastSyncTime() => DateTime.UtcNow;
        public void SetLastSyncTime(DateTime time) { }
    }

    private class FakeJobService : IJobService
    {
        public List<JobArgsBase> StartedJobs { get; } = new();

        public string StartJob(JobArgsBase args, string? inboxFilePath = null)
        {
            StartedJobs.Add(args);
            return Guid.NewGuid().ToString();
        }

        public void ForceStartJob(string id) { }
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => true;
        public bool ReportJobFailure(string id, string message) => true;
        public bool IsInboxFileTracked(string filePath) => false;
        public void Dispose() { }

        public JobItem? GetJob(string id) => null;
        public List<JobItem> GetJobs() => new();
        public List<JobItem> GetJobsForPlan(string planFile) => new();
        public List<JobItem> GetRecentJobs(int limit = 100) => new();
        public void PurgeHistoricalJobs() { }

#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
#pragma warning restore CS0067
    }

    private class FakePlanReader : IPlanReaderService
    {
        public List<PlanFile> Plans { get; set; } = new();

        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null) => Plans;

        public PlanFile? GetPlanByFolder(string folderPath)
        {
            return Plans.Find(p => folderPath.EndsWith(p.FolderName));
        }

        public string PlansDirectory => "/test/plans";
        public bool IsDatabaseReady => true;
        public void MigratePlans() { }
        public void RecoverStuckPlans() { }
        public List<PlanFile> GetIceboxPlans() => new();
        public void TransitionState(string folderName, PlanStatus newState) { }
        public void ResetToDraft(string folderName) { }
        public void ResetVerificationsForRetry(string folderName) { }
        public void SetVerificationStatus(string folderName, string name, VerificationStatus status) { }
        public void SaveRevision(string folderName, string content) { }
        public void RevertRevision(string folderName) { }
        public string ReadLatestRevision(string folderName) => "";
        public List<(int Number, string Content, DateTime Modified)> GetRevisions(string folderName) => new();
        public void DeletePlan(string folderName) { }
        public string ReadRawPlan(string folderName) => "";
        public void SavePlan(string folderName, string fullContent) { }
        public void UpdateLatestRevision(string folderName, string content) { }
        public DashboardModels GetDashboardData(string? projectFilter) => new DashboardModels(0, 0, 0, 0, 0, 0, 0, new(), new());
        public decimal GetPlanTotalCost(string folderPath) => 0;
        public int GetPlanTotalTokens(string folderPath) => 0;
        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null) => new();
        public List<Recommendation> GetRecommendations() => new();
        public int GetPendingRecommendationsCount() => 0;
        public PlanReaderService.PlanCountSnapshot ComputePlanCounts() => new(0, 0, 0, 0, 0, 0);
        public void UpdateRecommendationState(string planFolderName, string recommendationTitle, string newState, string? declineReason = null) { }
        public List<RecommendationYaml> GetRecommendationsForPlan(string folderName) => new();
        public void AcceptRecommendationAndRetry(string folderName, string recommendationTitle) { }
        public void AcceptRecommendationsAndRetry(string folderName, IReadOnlyCollection<string> titles) { }
        public void SyncPlanArtifacts(string planFolder) { }
        public void InvalidateCaches() { }
        public Task FlushPendingWritesAsync() => Task.CompletedTask;
        public event Action? CountsInvalidated;
    }

    private class FakePlanWatcher : IPlanWatcherService
    {
        public event Action<string?>? PlansChanged;

        public void RaisePlansChanged(string? folderName)
        {
            PlansChanged?.Invoke(folderName);
        }

        public void NotifyChanged(string? changedPlanFolder = null) { }
        public void Dispose() { }
    }

    [Theory]
    [InlineData("*/5 * * * *", "2026-07-17T15:25:00Z", true)]
    [InlineData("*/5 * * * *", "2026-07-17T15:26:00Z", false)]
    [InlineData("0 0 * * *", "2026-07-17T00:00:00Z", true)]
    [InlineData("0 0 * * *", "2026-07-17T12:00:00Z", false)]
    [InlineData("0 12 * * 1-5", "2026-07-20T12:00:00Z", true)] // Monday
    [InlineData("0 12 * * 1-5", "2026-07-19T12:00:00Z", false)] // Sunday
    [InlineData("30 14,18 * * *", "2026-07-17T14:30:00Z", true)]
    [InlineData("30 14,18 * * *", "2026-07-17T15:30:00Z", false)]
    [InlineData("0 0 * * 7", "2026-07-19T00:00:00Z", true)] // Sunday (7)
    public void CronMatcher_ShouldEvaluateCorrectly(string expression, string dateTimeStr, bool expected)
    {
        var dt = DateTime.Parse(dateTimeStr, null, DateTimeStyles.AdjustToUniversal);
        var result = CronMatcher.Matches(expression, dt);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CheckPlanForEventTriggers_NoPrs_ShouldTriggerImmediately()
    {
        // Arrange
        var db = new FakeDatabase();
        var jobService = new FakeJobService();
        var planReader = new FakePlanReader();
        var planWatcher = new FakePlanWatcher();
        var service = new WorkflowTriggerService(db, jobService, planReader, planWatcher, NullLogger<WorkflowTriggerService>.Instance);
        
        var plan = new PlanFile(
            new PlanMetadata(
                Id: 123,
                Project: "default",
                Level: "Feature",
                Title: "Test Plan",
                State: PlanStatus.Completed,
                Repos: new(),
                Commits: new(),
                Prs: new(), // No PRs
                Verifications: new(),
                RelatedPlans: new(),
                DependsOn: new(),
                Created: DateTime.UtcNow,
                Updated: DateTime.UtcNow,
                InitialPrompt: null,
                SourceUrl: null
            ),
            LatestRevisionContent: "",
            FolderPath: "/test/plans/123-TestPlan",
            PlanYamlRaw: ""
        );

        db.WorkflowsList.Add(new WorkflowItem
        {
            Id = 1,
            Name = "On Merged Workflow",
            IsActive = true,
            Project = "default",
            Definition = "{\"steps\":[{\"id\":\"start\",\"name\":\"Start\",\"type\":\"Trigger\",\"action\":\"event\",\"args\":\"plan_completed_and_merged\",\"next\":[]}]}"
        });

        // Act
        service.Start(); // Start with empty plans, so it is not in _triggeredPlanIds

        planReader.Plans.Add(plan); // Add plan now
        planWatcher.RaisePlansChanged("123-TestPlan");

        // Assert
        Assert.Single(jobService.StartedJobs);
        var job = jobService.StartedJobs[0] as WorkflowRunArgs;
        Assert.NotNull(job);
        Assert.Equal(1, job.WorkflowId);
        Assert.Equal("default", job.Project);
        Assert.Contains("plan_completed_and_merged", job.TriggerPayload);
    }

    [Fact]
    public void CheckPlanForEventTriggers_WithPrs_NotAllMerged_ShouldNotTrigger()
    {
        // Arrange
        var db = new FakeDatabase();
        var jobService = new FakeJobService();
        var planReader = new FakePlanReader();
        var planWatcher = new FakePlanWatcher();
        var service = new WorkflowTriggerService(db, jobService, planReader, planWatcher, NullLogger<WorkflowTriggerService>.Instance);
        
        var plan = new PlanFile(
            new PlanMetadata(
                Id: 124,
                Project: "default",
                Level: "Feature",
                Title: "Test Plan 2",
                State: PlanStatus.Completed,
                Repos: new(),
                Commits: new(),
                Prs: new() { "https://github.com/org/repo/pull/1" },
                Verifications: new(),
                RelatedPlans: new(),
                DependsOn: new(),
                Created: DateTime.UtcNow,
                Updated: DateTime.UtcNow,
                InitialPrompt: null,
                SourceUrl: null
            ),
            LatestRevisionContent: "",
            FolderPath: "/test/plans/124-TestPlan",
            PlanYamlRaw: ""
        );

        db.WorkflowsList.Add(new WorkflowItem
        {
            Id = 1,
            Name = "On Merged Workflow",
            IsActive = true,
            Project = "default",
            Definition = "{\"steps\":[{\"id\":\"start\",\"name\":\"Start\",\"type\":\"Trigger\",\"action\":\"event\",\"args\":\"plan_completed_and_merged\",\"next\":[]}]}"
        });

        // The PR is Open (not Merged)
        db.PrStatuses["https://github.com/org/repo/pull/1"] = "Open";

        // Act
        service.Start();
        planReader.Plans.Add(plan);
        planWatcher.RaisePlansChanged("124-TestPlan");

        // Assert
        Assert.Empty(jobService.StartedJobs);
    }

    [Fact]
    public void CheckPlanForEventTriggers_WithPrs_AllMerged_ShouldTrigger()
    {
        // Arrange
        var db = new FakeDatabase();
        var jobService = new FakeJobService();
        var planReader = new FakePlanReader();
        var planWatcher = new FakePlanWatcher();
        var service = new WorkflowTriggerService(db, jobService, planReader, planWatcher, NullLogger<WorkflowTriggerService>.Instance);
        
        var plan = new PlanFile(
            new PlanMetadata(
                Id: 125,
                Project: "default",
                Level: "Feature",
                Title: "Test Plan 3",
                State: PlanStatus.Completed,
                Repos: new(),
                Commits: new(),
                Prs: new() { "https://github.com/org/repo/pull/1" },
                Verifications: new(),
                RelatedPlans: new(),
                DependsOn: new(),
                Created: DateTime.UtcNow,
                Updated: DateTime.UtcNow,
                InitialPrompt: null,
                SourceUrl: null
            ),
            LatestRevisionContent: "",
            FolderPath: "/test/plans/125-TestPlan",
            PlanYamlRaw: ""
        );

        db.WorkflowsList.Add(new WorkflowItem
        {
            Id = 1,
            Name = "On Merged Workflow",
            IsActive = true,
            Project = "default",
            Definition = "{\"steps\":[{\"id\":\"start\",\"name\":\"Start\",\"type\":\"Trigger\",\"action\":\"event\",\"args\":\"plan_completed_and_merged\",\"next\":[]}]}"
        });

        // Start service while PR is Open
        db.PrStatuses["https://github.com/org/repo/pull/1"] = "Open";
        planReader.Plans.Add(plan);

        service.Start();

        // Now PR transitions to Merged, and PrStatusSyncService triggers check
        db.PrStatuses["https://github.com/org/repo/pull/1"] = "Merged";
        service.CheckAndTriggerPlanCompletedAndMerged("https://github.com/org/repo/pull/1");

        // Assert
        Assert.Single(jobService.StartedJobs);
        var job = jobService.StartedJobs[0] as WorkflowRunArgs;
        Assert.NotNull(job);
        Assert.Equal(1, job.WorkflowId);
        Assert.Contains("plan_completed_and_merged", job.TriggerPayload);
    }
}

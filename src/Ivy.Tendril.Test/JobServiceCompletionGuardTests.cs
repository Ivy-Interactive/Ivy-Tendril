using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceCompletionGuardTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }
    private static JobService CreateService()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));
    }

    private static JobService CreateServiceWithPlanReader(string plansDir)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            planReaderService: new StubPlanReaderService(plansDir));
    }

    private (JobService Service, StubPlanReaderService Plan) CreateServiceWithStub(PlanStatus? currentStatus = null)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var stub = new StubPlanReaderService(_tempDir.Path) { CurrentStatus = currentStatus };
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), planReaderService: stub);
        return (service, stub);
    }

    [Fact]
    public void StopJob_ExecutePlan_RevertsToPreviousDraft()
    {
        var (service, plan) = CreateServiceWithStub();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));
        service.GetJob(id)!.PreviousPlanState = PlanStatus.Draft;

        service.StopJob(id);

        Assert.Equal(JobStatus.Stopped, service.GetJob(id)!.Status);
        Assert.Contains(("test-plan", PlanStatus.Draft), plan.Transitions);
        Assert.DoesNotContain(plan.Transitions, t => t.State == PlanStatus.Review);
        Assert.Empty(plan.ResetToDraftCalls);
    }

    [Fact]
    public void StopJob_RetryPlan_RevertsToPreviousReview()
    {
        var (service, plan) = CreateServiceWithStub();
        var id = service.CreateTestJob(new RetryPlanArgs("test-plan", "fix it"));
        service.GetJob(id)!.PreviousPlanState = PlanStatus.Review;

        service.StopJob(id);

        Assert.Contains(("test-plan", PlanStatus.Review), plan.Transitions);
        Assert.Empty(plan.ResetToDraftCalls);
    }

    [Fact]
    public void StopJob_CreatePr_RevertsToPreviousReview()
    {
        var (service, plan) = CreateServiceWithStub();
        var id = service.CreateTestJob(new CreatePrArgs("test-plan"));
        // CreatePr starts from Review; snapshot lets it revert there (no fallback covers CreatePr).
        service.GetJob(id)!.PreviousPlanState = PlanStatus.Review;

        service.StopJob(id);

        Assert.Contains(("test-plan", PlanStatus.Review), plan.Transitions);
        Assert.DoesNotContain(plan.Transitions, t => t.State == PlanStatus.Creating);
    }

    [Fact]
    public void StopJob_UsesFallbackWhenSnapshotMissing()
    {
        var (service, plan) = CreateServiceWithStub();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));
        // PreviousPlanState left null (e.g. after restart) → fallback map → Draft.

        service.StopJob(id);

        Assert.Contains(("test-plan", PlanStatus.Draft), plan.Transitions);
    }

    [Fact]
    public void DeleteJob_ExecutePlan_NonTerminal_ResetsToDraft()
    {
        var (service, plan) = CreateServiceWithStub(currentStatus: PlanStatus.Review);
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));

        service.DeleteJob(id);

        Assert.Contains("test-plan", plan.ResetToDraftCalls);
    }

    [Fact]
    public void DeleteJob_ExecutePlan_Terminal_DoesNotResetToDraft()
    {
        var (service, plan) = CreateServiceWithStub(currentStatus: PlanStatus.Completed);
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));
        service.GetJob(id)!.PreviousPlanState = PlanStatus.Draft;

        service.DeleteJob(id);

        Assert.Empty(plan.ResetToDraftCalls);
    }

    [Fact]
    public void DeleteJob_RetryPlan_RevertsToReviewWithoutReset()
    {
        var (service, plan) = CreateServiceWithStub(currentStatus: PlanStatus.Review);
        var id = service.CreateTestJob(new RetryPlanArgs("test-plan", "again"));
        service.GetJob(id)!.PreviousPlanState = PlanStatus.Review;

        service.DeleteJob(id);

        Assert.Empty(plan.ResetToDraftCalls);
        Assert.Contains(("test-plan", PlanStatus.Review), plan.Transitions);
    }

    [Fact]
    public void CompleteJob_CancelledExecutePlan_RevertsInsteadOfReview()
    {
        var (service, plan) = CreateServiceWithStub();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));
        var job = service.GetJob(id)!;
        job.PreviousPlanState = PlanStatus.Draft;
        job.CancellationRequested = true;

        // Completion path wins the race after cancellation was requested.
        service.CompleteJob(id, 0);

        Assert.Contains(("test-plan", PlanStatus.Draft), plan.Transitions);
        Assert.DoesNotContain(plan.Transitions, t => t.State == PlanStatus.Review);
    }

    [Fact]
    public void CompleteJob_FailedExecutePlan_RevertsAndKeepsWorktree()
    {
        var (service, plan) = CreateServiceWithStub();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));
        service.GetJob(id)!.PreviousPlanState = PlanStatus.Draft;

        service.CompleteJob(id, 1);

        Assert.Equal(JobStatus.Failed, service.GetJob(id)!.Status);
        Assert.Contains(("test-plan", PlanStatus.Draft), plan.Transitions);
        Assert.DoesNotContain(plan.Transitions, t => t.State is PlanStatus.Failed or PlanStatus.Review);
    }

    [Fact]
    public void CompleteJob_SuccessWithIncompleteVerifications_TransitionsToFailed()
    {
        // The success path is now the only producer of PlanStatus.Failed: execution
        // completed (exit 0) but a verification is still Pending.
        var (service, plan) = CreateServiceWithStub();
        var planFolder = Path.Combine(_tempDir.Path, "00777-VerFail");
        Directory.CreateDirectory(planFolder);
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"),
            "state: Executing\nproject: Test\nverifications:\n- name: DotnetBuild\n  status: Pending\n");

        var id = service.CreateTestJob(new ExecutePlanArgs(planFolder));

        service.CompleteJob(id, 0);

        Assert.Contains(("00777-VerFail", PlanStatus.Failed), plan.Transitions);
        Assert.DoesNotContain(plan.Transitions, t => t.State == PlanStatus.Review);
    }

    [Fact]
    public void CompleteJob_ConcurrentCalls_OnlyFirstCompletes()
    {
        var service = CreateService();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));

        var notificationCount = 0;
        service.NotificationReady += _ => Interlocked.Increment(ref notificationCount);

        using var barrier = new Barrier(2);
        var statuses = new JobStatus?[2];

        var t1 = new Thread(() =>
        {
            barrier.SignalAndWait();
            service.CompleteJob(id, 0);
            statuses[0] = service.GetJob(id)?.Status;
        });

        var t2 = new Thread(() =>
        {
            barrier.SignalAndWait();
            service.CompleteJob(id, 1);
            statuses[1] = service.GetJob(id)?.Status;
        });

        t1.Start();
        t2.Start();
        t1.Join(TimeSpan.FromSeconds(5));
        t2.Join(TimeSpan.FromSeconds(5));

        var job = service.GetJob(id);
        Assert.NotNull(job);
        // Only one thread should have completed the job — status should be either Completed or Failed, not both
        Assert.True(job.Status is JobStatus.Completed or JobStatus.Failed);
        // Only one notification should have fired
        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public void StopJob_RacingWithCompleteJob_OnlyOneWins()
    {
        var service = CreateService();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));

        var notificationCount = 0;
        service.NotificationReady += _ => Interlocked.Increment(ref notificationCount);

        using var barrier = new Barrier(2);

        var t1 = new Thread(() =>
        {
            barrier.SignalAndWait();
            service.StopJob(id);
        });

        var t2 = new Thread(() =>
        {
            barrier.SignalAndWait();
            service.CompleteJob(id, 0);
        });

        t1.Start();
        t2.Start();
        t1.Join(TimeSpan.FromSeconds(5));
        t2.Join(TimeSpan.FromSeconds(5));

        var job = service.GetJob(id);
        Assert.NotNull(job);
        // Status should be one terminal state — not corrupted
        Assert.True(job.Status is JobStatus.Stopped or JobStatus.Completed);
    }

    [Fact]
    public void CompleteJob_AfterStopJob_IsNoOp()
    {
        var service = CreateService();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));

        service.StopJob(id);
        var job = service.GetJob(id);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Stopped, job.Status);

        // CompleteJob after StopJob should be a no-op
        service.CompleteJob(id, 0);

        job = service.GetJob(id);
        Assert.Equal(JobStatus.Stopped, job!.Status);
    }

    [Fact]
    public void CompleteJob_StaleOutputDetected_SetsTimeoutWithStaleReason()
    {
        var service = CreateService();
        var id = service.CreateTestJob(new ExecutePlanArgs("test-plan"));

        var job = service.GetJob(id);
        Assert.NotNull(job);
        job.StaleOutputDetected = true;

        service.CompleteJob(id, null, true, true);

        job = service.GetJob(id);
        Assert.NotNull(job);
        Assert.Equal(JobStatus.Timeout, job.Status);
        Assert.Contains("No output for 10 minutes", job.StatusMessage);
    }

    [Fact]
    public void CompleteJob_CreatePlan_UpdatesPlanFileWhenOutputContainsPlanCreated()
    {
        var service = CreateServiceWithPlanReader(_tempDir.Path);
        var id = service.CreateTestJob(new CreatePlanArgs("Fix login bug", "Tendril"));

        var job = service.GetJob(id);
        Assert.NotNull(job);
        job.EnqueueOutput("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Processing...\"}]}}");
        job.EnqueueOutput("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Plan created: 02353-FixLoginBug\"}]}}");

        service.CompleteJob(id, 0);

        job = service.GetJob(id);
        Assert.NotNull(job);
        Assert.Equal("02353-FixLoginBug", job.PlanFile);
    }

    [Fact]
    public void CompleteJob_CreatePlan_LeavesPlanFileUnchangedOnDuplicate()
    {
        var service = CreateServiceWithPlanReader(_tempDir.Path);
        var id = service.CreateTestJob(new CreatePlanArgs("Fix login bug", "Tendril"));

        var job = service.GetJob(id);
        Assert.NotNull(job);
        var originalPlanFile = job.PlanFile;
        job.EnqueueOutput("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Processing...\"}]}}");
        job.EnqueueOutput("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"identified as duplicate: 01234-ExistingPlan\"}]}}");

        service.CompleteJob(id, 0);

        job = service.GetJob(id);
        Assert.NotNull(job);
        Assert.Equal(originalPlanFile, job.PlanFile);
        Assert.Equal(JobStatus.Completed, job.Status);
    }

    private class StubPlanReaderService(string plansDirectory) : IPlanReaderService
    {
        public string PlansDirectory => plansDirectory;
        public bool IsDatabaseReady => true;
#pragma warning disable CS0067
        public event Action? CountsInvalidated;
#pragma warning restore CS0067

        // Recording hooks for plan-state assertions.
        public readonly List<(string Folder, PlanStatus State)> Transitions = new();
        public readonly List<string> ResetToDraftCalls = new();

        // Status returned by GetPlanByFolder (simulates the plan's current state).
        public PlanStatus? CurrentStatus { get; set; }

        public void MigratePlanStateNames()
        {
        }

        public void RecoverStuckPlans()
        {
        }

        public void RepairPlans()
        {
        }

        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null)
        {
            return [];
        }

        public PlanFile? GetPlanByFolder(string folderPath)
        {
            if (CurrentStatus is not { } status) return null;
            var metadata = new PlanMetadata(0, "", "", "", status, [], [], [], [], [], [],
                default, default, null, null);
            return new PlanFile(metadata, "", folderPath, "", 1);
        }

        public List<PlanFile> GetIceboxPlans()
        {
            return [];
        }

        public void TransitionState(string folderName, PlanStatus newState)
        {
            Transitions.Add((folderName, newState));
        }

        public void ResetToDraft(string folderName)
        {
            ResetToDraftCalls.Add(folderName);
        }

        public void ResetVerificationsForRetry(string folderName)
        {
        }

        public void SetVerificationStatus(string folderName, string name, VerificationStatus status)
        {
        }

        public void RevertRevision(string folderName)
        {
        }

        public void SaveRevision(string folderName, string content)
        {
        }

        public string ReadLatestRevision(string folderName)
        {
            return "";
        }

        public List<(int Number, string Content, DateTime Modified)> GetRevisions(string folderName)
        {
            return [];
        }

        public void AddLog(string folderName, string action, string content, string? jobId = null)
        {
        }

        public void DeletePlan(string folderName)
        {
        }

        public string ReadRawPlan(string folderName)
        {
            return "";
        }

        public void SavePlan(string folderName, string fullContent)
        {
        }

        public void UpdateLatestRevision(string folderName, string content)
        {
        }

        public DashboardModels GetDashboardData(string? projectFilter)
        {
            return new DashboardModels(0, 0, 0, 0, 0, 0, 0, [], []);
        }

        public decimal GetPlanTotalCost(string folderPath)
        {
            return 0;
        }

        public int GetPlanTotalTokens(string folderPath)
        {
            return 0;
        }

        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null)
        {
            return [];
        }

        public List<Recommendation> GetRecommendations()
        {
            return [];
        }

        public int GetPendingRecommendationsCount()
        {
            return 0;
        }

        public PlanReaderService.PlanCountSnapshot ComputePlanCounts()
        {
            return new PlanReaderService.PlanCountSnapshot(0, 0, 0, 0, 0, 0);
        }

        public void UpdateRecommendationState(string planFolderName, string recommendationTitle, string newState,
            string? declineReason = null)
        {
        }

        public void SyncPlanArtifacts(string planFolder)
        {
        }

        public void InvalidateCaches()
        {
        }

        public Task FlushPendingWritesAsync()
        {
            return Task.CompletedTask;
        }

        public List<RecommendationYaml> GetRecommendationsForPlan(string folderName)
        {
            return [];
        }

        public void AcceptRecommendationAndRetry(string folderName, string recommendationTitle)
        {
        }
    }
}
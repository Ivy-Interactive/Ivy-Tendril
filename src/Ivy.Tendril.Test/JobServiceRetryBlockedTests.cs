using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.Helpers;

namespace Ivy.Tendril.Test;

public class JobServiceRetryBlockedTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private string CreatePlanFolder(string state, List<string>? dependsOn = null, List<string>? prs = null)
    {
        var planDir = Path.Combine(_tempDir.Path, $"plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(planDir);
        var repoDir = Path.Combine(planDir, "repo");
        Directory.CreateDirectory(repoDir);

        string depsYaml;
        if (dependsOn is { Count: > 0 })
            depsYaml = "dependsOn:\n" + string.Join("\n", dependsOn.Select(d => $"- {d}"));
        else
            depsYaml = "dependsOn: []";

        string prsYaml;
        if (prs is { Count: > 0 })
            prsYaml = "prs:\n" + string.Join("\n", prs.Select(p => $"- {p}"));
        else
            prsYaml = "prs: []";

        var yaml =
            $"state: {state}\nproject: TestProject\nlevel: NiceToHave\ntitle: Test\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\n{depsYaml}\n{prsYaml}\ncommits: []\nverifications: []\nrelatedPlans: []\nrepos:\n- {repoDir}\n";
        File.WriteAllText(Path.Combine(planDir, "plan.yaml"), yaml);
        return planDir;
    }

    private string CreatePlansDirectory(params (string folderName, string state)[] plans)
    {
        var plansDir = Path.Combine(_tempDir.Path, $"plans-{Guid.NewGuid():N}");
        Directory.CreateDirectory(plansDir);

        foreach (var (folderName, state) in plans)
        {
            var planDir = Path.Combine(plansDir, folderName);
            Directory.CreateDirectory(planDir);
            var repoDir = Path.Combine(planDir, "repo");
            Directory.CreateDirectory(repoDir);
            var yaml =
                $"state: {state}\nproject: TestProject\nlevel: NiceToHave\ntitle: {folderName}\ncreated: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\ndependsOn: []\nprs: []\ncommits: []\nverifications: []\nrelatedPlans: []\nrepos:\n- {repoDir}\n";
            File.WriteAllText(Path.Combine(planDir, "plan.yaml"), yaml);
        }

        return plansDir;
    }

    [Fact]
    public void RetryBlockedJobs_WhenDependencySatisfied_AutoStartsJob()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        // Create plans directory with a completed dependency
        var plansDir = CreatePlansDirectory(("01100-DepPlan", "Completed"));

        // Create the dependent plan that depends on 01100-DepPlan
        var dependentPlan = CreatePlanFolder("Draft", ["01100-DepPlan"]);

        var planReader = new FakePlanReaderService(plansDir);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Manually create a blocked job (simulating what StartJob does when dependencies aren't met)
        var blockedId = service.CreateTestJob(new ExecutePlanArgs(dependentPlan));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;

        // Create a completing CreatePr job to trigger RetryBlockedJobs
        var completingId = service.CreateTestJob(new CreatePrArgs(Path.GetTempPath()));

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        // Complete the CreatePr job successfully — this should trigger RetryBlockedJobs
        service.CompleteJob(completingId, 0);

        // The blocked job should have been removed
        Assert.Null(service.GetJob(blockedId));

        // A new job should have been created (the restarted one)
        var jobs = service.GetJobs();
        // The restarted job won't actually launch (no script), but it should exist
        // Since dependencies are now satisfied, it should NOT be blocked
        var restartedJob = jobs.FirstOrDefault(j => j.Id != completingId && j.Type == "ExecutePlan");
        Assert.NotNull(restartedJob);
        Assert.NotEqual(JobStatus.Blocked, restartedJob.Status);

        // Should have received an "unblocked" notification
        Assert.Contains(notifications, n => n.Title == "Job Unblocked");

        // Cleanup
        Directory.Delete(dependentPlan, true);
        Directory.Delete(plansDir, true);
    }

    [Fact]
    public void RetryBlockedJobs_WhenDependencyStillUnsatisfied_DoesNothing()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        // Create plans directory with an incomplete dependency
        var plansDir = CreatePlansDirectory(("01100-DepPlan", "Executing"));

        // Create the dependent plan
        var dependentPlan = CreatePlanFolder("Draft", ["01100-DepPlan"]);

        var planReader = new FakePlanReaderService(plansDir);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Create a blocked job
        var blockedId = service.CreateTestJob(new ExecutePlanArgs(dependentPlan));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;

        // Create a completing job
        var completingId = service.CreateTestJob(new CreatePrArgs(Path.GetTempPath()));

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        // Complete the CreatePr job
        service.CompleteJob(completingId, 0);

        // The blocked job should still exist and still be blocked
        var stillBlocked = service.GetJob(blockedId);
        Assert.NotNull(stillBlocked);
        Assert.Equal(JobStatus.Blocked, stillBlocked.Status);

        // Should NOT have received an "unblocked" notification
        Assert.DoesNotContain(notifications, n => n.Title == "Job Unblocked");

        // Cleanup
        Directory.Delete(dependentPlan, true);
        Directory.Delete(plansDir, true);
    }

    [Fact]
    public void RetryBlockedJobs_WhenJobAlreadyRemoved_DoesNotCreateDuplicate()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        // Create plans directory with a completed dependency
        var plansDir = CreatePlansDirectory(("01100-DepPlan", "Completed"));

        // Create two dependent plans that depend on the same dep
        var dependentPlan1 = CreatePlanFolder("Draft", ["01100-DepPlan"]);
        var dependentPlan2 = CreatePlanFolder("Draft", ["01100-DepPlan"]);

        var planReader = new FakePlanReaderService(plansDir);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Create a blocked job for plan1
        var blockedId = service.CreateTestJob(new ExecutePlanArgs(dependentPlan1));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;

        // Manually remove the blocked job before CompleteJob triggers RetryBlockedJobs
        // This simulates another thread having already handled it
        service.GetJobs(); // ensure enumeration snapshot
        var removed = service.RemoveJob(blockedId);
        Assert.True(removed);

        // Create a completing job to trigger RetryBlockedJobs
        var completingId = service.CreateTestJob(new CreatePrArgs(Path.GetTempPath()));
        service.CompleteJob(completingId, 0);

        // Since the blocked job was already removed, no new ExecutePlan job should be created for it
        var jobs = service.GetJobs();
        var executePlanJobs =
            jobs.Where(j => j.Type == "ExecutePlan" && j.TypedArgs?.PlanFolder == dependentPlan1).ToList();
        Assert.Empty(executePlanJobs);

        // Cleanup
        Directory.Delete(dependentPlan1, true);
        Directory.Delete(dependentPlan2, true);
        Directory.Delete(plansDir, true);
    }

    [Fact]
    public void RetryBlockedJobs_WhenActiveJobExistsForSamePlan_SkipsRetry()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        // Create plans directory with a completed dependency
        var plansDir = CreatePlansDirectory(("01100-DepPlan", "Completed"));

        // Create the dependent plan
        var dependentPlan = CreatePlanFolder("Draft", ["01100-DepPlan"]);

        var planReader = new FakePlanReaderService(plansDir);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Create a Running ExecutePlan job for the same plan folder (simulating an already active job)
        var activeId = service.CreateTestJob(new ExecutePlanArgs(dependentPlan));
        Assert.Equal(JobStatus.Running, service.GetJob(activeId)!.Status);

        // Create a blocked job for the same plan folder
        var blockedId = service.CreateTestJob(new ExecutePlanArgs(dependentPlan));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;

        // Create a completing job to trigger RetryBlockedJobs
        var completingId = service.CreateTestJob(new CreatePrArgs(Path.GetTempPath()));
        service.CompleteJob(completingId, 0);

        // The blocked job should remain because an active job exists for the same plan
        var stillBlocked = service.GetJob(blockedId);
        Assert.NotNull(stillBlocked);
        Assert.Equal(JobStatus.Blocked, stillBlocked.Status);

        // The active job should still be running
        var executePlanJobs = service.GetJobs()
            .Where(j => j.Type == "ExecutePlan" && j.TypedArgs?.PlanFolder == dependentPlan)
            .ToList();

        Assert.Equal(2, executePlanJobs.Count);
        Assert.Contains(executePlanJobs, j => j.Id == activeId && j.Status == JobStatus.Running);
        Assert.Contains(executePlanJobs, j => j.Id == blockedId && j.Status == JobStatus.Blocked);

        // Cleanup
        Directory.Delete(dependentPlan, true);
        Directory.Delete(plansDir, true);
    }

    [Fact]
    public void RetryBlockedJobs_WhenBlockingJobFails_AutoRetriesBlockedJob()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        // Create two plan folders that will compete for the same repo
        var planA = Path.Combine(_tempDir.Path, "plan-A");
        var planB = Path.Combine(_tempDir.Path, "plan-B");
        Directory.CreateDirectory(planA);
        Directory.CreateDirectory(planB);

        // Both plans target the same repo
        var repo = Path.Combine(_tempDir.Path, "shared-repo");
        Directory.CreateDirectory(repo);

        var planYaml = $"state: Draft\nproject: TestProject\nlevel: NiceToHave\ntitle: Test\nupdated: 2026-01-01T00:00:00Z\nrepos:\n- {repo}\ndependsOn: []\nprs: []\ncommits: []\nverifications: []\nrelatedPlans: []\n";
        File.WriteAllText(Path.Combine(planA, "plan.yaml"), planYaml);
        File.WriteAllText(Path.Combine(planB, "plan.yaml"), planYaml);

        var planReader = new FakePlanReaderService(_tempDir.Path);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Start job A (this will hold the repo lock)
        var jobAId = service.CreateTestJob(new ExecutePlanArgs(planA));
        Assert.Equal(JobStatus.Running, service.GetJob(jobAId)!.Status);

        // Attempt to start job B (should get blocked due to repo concurrency)
        var jobBId = service.CreateTestJob(new ExecutePlanArgs(planB));
        var blockedJob = service.GetJob(jobBId)!;
        blockedJob.Status = JobStatus.Blocked;

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        // Fail job A — this should release the lock and auto-retry job B
        service.CompleteJob(jobAId, 1);

        // Job B should have been removed from blocked state
        Assert.Null(service.GetJob(jobBId));

        // A new job should have been created (the restarted job B)
        var jobs = service.GetJobs();
        var restartedJob = jobs.FirstOrDefault(j => j.Id != jobAId && j.Type == "ExecutePlan" && j.TypedArgs?.PlanFolder == planB);
        Assert.NotNull(restartedJob);
        Assert.NotEqual(JobStatus.Blocked, restartedJob.Status);

        // Should have received an "unblocked" notification
        Assert.Contains(notifications, n => n.Title == "Job Unblocked");
    }

    [Fact]
    public void RetryBlockedJobs_WhenBlockingJobCancelled_AutoRetriesBlockedJob()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        // Create two plan folders that will compete for the same repo
        var planA = Path.Combine(_tempDir.Path, "plan-A-cancel");
        var planB = Path.Combine(_tempDir.Path, "plan-B-cancel");
        Directory.CreateDirectory(planA);
        Directory.CreateDirectory(planB);

        // Both plans target the same repo
        var repo = Path.Combine(_tempDir.Path, "shared-repo-cancel");
        Directory.CreateDirectory(repo);

        var planYaml = $"state: Draft\nproject: TestProject\nlevel: NiceToHave\ntitle: Test\nupdated: 2026-01-01T00:00:00Z\nrepos:\n- {repo}\ndependsOn: []\nprs: []\ncommits: []\nverifications: []\nrelatedPlans: []\n";
        File.WriteAllText(Path.Combine(planA, "plan.yaml"), planYaml);
        File.WriteAllText(Path.Combine(planB, "plan.yaml"), planYaml);

        var planReader = new FakePlanReaderService(_tempDir.Path);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Start job A (this will hold the repo lock)
        var jobAId = service.CreateTestJob(new ExecutePlanArgs(planA));
        Assert.Equal(JobStatus.Running, service.GetJob(jobAId)!.Status);

        // Attempt to start job B (should get blocked due to repo concurrency)
        var jobBId = service.CreateTestJob(new ExecutePlanArgs(planB));
        var blockedJob = service.GetJob(jobBId)!;
        blockedJob.Status = JobStatus.Blocked;

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        // Cancel job A — this should release the lock and auto-retry job B
        service.StopJob(jobAId);

        // Job B should have been removed from blocked state
        Assert.Null(service.GetJob(jobBId));

        // A new job should have been created (the restarted job B)
        var jobs = service.GetJobs();
        var restartedJob = jobs.FirstOrDefault(j => j.Id != jobAId && j.Type == "ExecutePlan" && j.TypedArgs?.PlanFolder == planB);
        Assert.NotNull(restartedJob);
        Assert.NotEqual(JobStatus.Blocked, restartedJob.Status);

        // Should have received an "unblocked" notification
        Assert.Contains(notifications, n => n.Title == "Job Unblocked");
    }

    [Fact]
    public void RetryBlockedJobs_WhenSamePlanFolderJobRunning_DoesNotRetryBlockedJob()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        // Same plan folder — this should still block (worktree conflict)
        var planA = Path.Combine(_tempDir.Path, "plan-A-same");
        Directory.CreateDirectory(planA);

        var repo = Path.Combine(planA, "repo");
        Directory.CreateDirectory(repo);

        var planYaml = $"state: Draft\nproject: TestProject\nlevel: NiceToHave\ntitle: Test\nupdated: 2026-01-01T00:00:00Z\nrepos:\n- {repo}\ndependsOn: []\nprs: []\ncommits: []\nverifications: []\nrelatedPlans: []\n";
        File.WriteAllText(Path.Combine(planA, "plan.yaml"), planYaml);

        var planReader = new FakePlanReaderService(_tempDir.Path);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Start job on planA
        var jobRunningId = service.CreateTestJob(new ExecutePlanArgs(planA));
        Assert.Equal(JobStatus.Running, service.GetJob(jobRunningId)!.Status);

        // Manually create a blocked job for same plan folder
        var blockedId = service.CreateTestJob(new ExecutePlanArgs(planA));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;

        // Create a completing CreatePr job to trigger RetryBlockedJobs
        var completingId = service.CreateTestJob(new CreatePrArgs(Path.GetTempPath()));

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        service.CompleteJob(completingId, 0);

        // Job should still be blocked — same plan folder has an active job
        var stillBlocked = service.GetJob(blockedId);
        Assert.NotNull(stillBlocked);
        Assert.Equal(JobStatus.Blocked, stillBlocked.Status);

        Assert.DoesNotContain(notifications, n => n.Title == "Job Unblocked");
    }

    [Fact]
    public void DeleteJob_WhenDeletedJobWasDependency_RetryBlockedJobs()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        // Create plans directory with a completed dependency
        var plansDir = CreatePlansDirectory(("01100-DepPlan", "Completed"));

        // Create the dependent plan
        var dependentPlan = CreatePlanFolder("Draft", ["01100-DepPlan"]);

        var planReader = new FakePlanReaderService(plansDir);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Create a blocked job for the dependent plan
        var blockedId = service.CreateTestJob(new ExecutePlanArgs(dependentPlan));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;

        // Create a completed ExecutePlan job (simulating a completed dependency)
        var completedId = service.CreateTestJob(new ExecutePlanArgs(Path.Combine(plansDir, "01100-DepPlan")));
        var completedJob = service.GetJob(completedId)!;
        completedJob.Status = JobStatus.Completed;

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        // Delete the completed dependency job — this should trigger RetryBlockedJobs
        service.DeleteJob(completedId);

        // The blocked job should have been removed and restarted
        Assert.Null(service.GetJob(blockedId));

        // A new job should have been created (the restarted one)
        var jobs = service.GetJobs();
        var restartedJob = jobs.FirstOrDefault(j => j.Id != completedId && j.Type == "ExecutePlan");
        Assert.NotNull(restartedJob);
        Assert.NotEqual(JobStatus.Blocked, restartedJob.Status);

        // Should have received an "unblocked" notification
        Assert.Contains(notifications, n => n.Title == "Job Unblocked");

        // Cleanup
        Directory.Delete(dependentPlan, true);
        Directory.Delete(plansDir, true);
    }


    [Fact]
    public void RetryBlockedJobs_WhenBlockedOnWaitForJobs_DoesNotChurnOrEmitSpuriousNotifications()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var planFolder = CreatePlanFolder("Draft");

        var planReader = new FakePlanReaderService(_tempDir.Path);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Long-running dependency job (stays Running)
        var depId = service.CreateTestJob(new CreatePlanArgs("Dep job", "Auto"));
        Assert.Equal(JobStatus.Running, service.GetJob(depId)!.Status);

        // The waiting job blocks on the sibling job, not on plan-level dependsOn
        var waitingId = service.StartJob(new ExecutePlanArgs(planFolder) { WaitForJobs = [depId] });
        var waitingJob = service.GetJob(waitingId);
        Assert.NotNull(waitingJob);
        Assert.Equal(JobStatus.Blocked, waitingJob.Status);

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        // Drive the 60-second blocked-job-check timer body deterministically
        service.RunBlockedJobCheck();

        // The job must be left exactly as it was — no churn, still blocked on the same dependency
        var stillWaiting = service.GetJob(waitingId);
        Assert.NotNull(stillWaiting);
        Assert.Equal(JobStatus.Blocked, stillWaiting.Status);
        Assert.Contains(depId, stillWaiting.StatusMessage);

        Assert.DoesNotContain(notifications, n => n.Title == "Job Unblocked");
        Assert.DoesNotContain(notifications, n => n.Title == "Job Blocked");

        // Cleanup
        Directory.Delete(planFolder, true);
    }

    [Fact]
    public async Task PeriodicCheck_WhenDependencySatisfiedExternally_UnblocksJob()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        // Create plans directory with an incomplete dependency
        var plansDir = CreatePlansDirectory(("01100-DepPlan", "Executing"));

        // Create the dependent plan
        var dependentPlan = CreatePlanFolder("Draft", ["01100-DepPlan"]);

        var planReader = new FakePlanReaderService(plansDir);
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader);

        // Create a blocked job
        var blockedId = service.CreateTestJob(new ExecutePlanArgs(dependentPlan));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;

        var notifications = new List<JobNotification>();
        service.NotificationReady += n => notifications.Add(n);

        // Simulate the dependency plan being completed externally (e.g., manual PR merge)
        var depPlanPath = Path.Combine(plansDir, "01100-DepPlan", "plan.yaml");
        var depYaml = File.ReadAllText(depPlanPath);
        depYaml = depYaml.Replace("state: Executing", "state: Completed");
        File.WriteAllText(depPlanPath, depYaml);

        // Drive the periodic dependency re-check directly instead of waiting for the 60s timer
        // (deterministic and instant — the timer body is identical to RunBlockedJobCheck).
        service.RunBlockedJobCheck();
        RetryHelper.WaitUntil(() => service.GetJob(blockedId) == null, TimeSpan.FromSeconds(10));

        // The blocked job should have been removed and restarted
        Assert.Null(service.GetJob(blockedId));

        // A new job should have been created (the restarted one)
        var jobs = service.GetJobs();
        var restartedJob = jobs.FirstOrDefault(j => j.Id != blockedId && j.Type == "ExecutePlan");
        Assert.NotNull(restartedJob);
        Assert.NotEqual(JobStatus.Blocked, restartedJob.Status);

        // Should have received an "unblocked" notification
        Assert.Contains(notifications, n => n.Title == "Job Unblocked");

        // Cleanup
        Directory.Delete(dependentPlan, true);
        Directory.Delete(plansDir, true);
    }

    [Fact]
    public void RetryBlockedJobs_WhenDependenciesSatisfied_DeletesBlockedJobFromDatabase()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var plansDir = CreatePlansDirectory(("01100-DepPlan", "Completed"));
        var dependentPlan = CreatePlanFolder("Draft", ["01100-DepPlan"]);

        var planReader = new FakePlanReaderService(plansDir);
        var db = new FakeDatabaseService();
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            planReaderService: planReader,
            database: db);

        var blockedId = service.CreateTestJob(new ExecutePlanArgs(dependentPlan));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;

        var completingId = service.CreateTestJob(new CreatePrArgs(Path.GetTempPath()));

        service.CompleteJob(completingId, 0);

        Assert.Null(service.GetJob(blockedId));
        Assert.Contains(blockedId, db.DeletedJobIds);

        Directory.Delete(dependentPlan, true);
        Directory.Delete(plansDir, true);
    }

    [Fact]
    public void ForceStartJob_BlockedJob_DeletesOldRecordFromDatabase()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var db = new FakeDatabaseService();
        var service = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            database: db);

        var blockedId = service.CreateTestJob(new ExecutePlanArgs("/path/to/plan"));
        var blockedJob = service.GetJob(blockedId)!;
        blockedJob.Status = JobStatus.Blocked;

        service.ForceStartJob(blockedId);

        Assert.Null(service.GetJob(blockedId));
        Assert.Contains(blockedId, db.DeletedJobIds);
    }

    /// <summary>
    ///     Minimal fake that provides PlansDirectory for dependency checking.
    /// </summary>
    private class FakePlanReaderService : IPlanReaderService
    {
        public FakePlanReaderService(string plansDirectory)
        {
            PlansDirectory = plansDirectory;
        }

        public string PlansDirectory { get; }
        public bool IsDatabaseReady => true;
#pragma warning disable CS0067
        public event Action? CountsInvalidated;
#pragma warning restore CS0067

        public void MigratePlans()
        {
        }

        public void RecoverStuckPlans()
        {
        }

        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null)
        {
            return [];
        }

        public PlanFile? GetPlanByFolder(string folderPath)
        {
            return null;
        }

        public List<PlanFile> GetIceboxPlans()
        {
            return [];
        }

        public void TransitionState(string folderName, PlanStatus newState)
        {
        }

        public IReadOnlyList<string> GetFailedVerifications(string folderName) => [];
        public void CompleteWithPartialDelivery(string folderName) { }

        public void ResetToDraft(string folderName)
        {
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

        public void AcceptRecommendationsAndRetry(string folderName, IReadOnlyCollection<string> titles)
        {
        }
    }

    private class FakeDatabaseService : IPlanDatabaseService
    {
        public List<JobItem> Jobs { get; } = new();
        public List<string> DeletedJobIds { get; } = new();
        public List<string> UpsertedJobIds { get; } = new();

        public List<JobItem> GetRecentJobs(int limit = 100)
        {
            return Jobs.ToList();
        }

        public JobItem? GetJobById(string id)
        {
            return Jobs.FirstOrDefault(j => j.Id == id);
        }

        public List<JobItem> GetJobsForPlan(string planFile)
        {
            return Jobs.Where(j => j.PlanFile == planFile).ToList();
        }

        public void DeleteJob(string id)
        {
            DeletedJobIds.Add(id);
            Jobs.RemoveAll(j => j.Id == id);
        }

        public void Dispose()
        {
        }

        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null)
        {
            return [];
        }

        public PlanFile? GetPlanByFolder(string folderPath)
        {
            return null;
        }

        public PlanFile? GetPlanById(int planId)
        {
            return null;
        }

        public PlanReaderService.PlanCountSnapshot ComputePlanCounts()
        {
            return new PlanReaderService.PlanCountSnapshot(0, 0, 0, 0, 0, 0);
        }

        public DashboardModels GetDashboardData(string? projectFilter)
        {
            return new DashboardModels(0, 0, 0, 0, 0, 0, 0, [], []);
        }

        public List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days = 30)
        {
            return [];
        }

        public decimal GetPlanTotalCost(int planId)
        {
            return 0;
        }

        public int GetPlanTotalTokens(int planId)
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

        public List<PlanFile> SearchPlans(string query)
        {
            return [];
        }

        public void RebuildFtsIndex()
        {
        }

        public void UpdatePlanState(int planId, PlanStatus state)
        {
        }

        public void UpdatePlanContent(int planId, string latestRevisionContent, int revisionCount)
        {
        }

        public void UpdateRecommendationState(int planId, string recommendationTitle, string newState, string? declineReason)
        {
        }

        public void UpsertPlan(PlanFile plan)
        {
        }

        public void DeletePlan(int planId)
        {
        }

        public void UpsertCosts(int planId, List<CostEntry> costs)
        {
        }

        public void UpsertRecommendations(int planId, string folderName, List<RecommendationYaml> recommendations,
            string project, string planTitle, DateTime updated, PlanStatus status)
        {
        }

        public void BulkUpsertPlans(List<PlanFile> plans, bool forceOverwrite = false)
        {
        }

        public HashSet<int> GetTerminalPlanIds()
        {
            return [];
        }

        public void UpsertJob(JobItem job)
        {
            UpsertedJobIds.Add(job.Id);
            Jobs.RemoveAll(j => j.Id == job.Id);
            Jobs.Add(job);
        }

        public List<string> PurgeOldJobs(int keepCount = 500)
        {
            return [];
        }

        public Dictionary<string, PrInfo> GetAllPrStatuses()
        {
            return new Dictionary<string, PrInfo>();
        }

        public void UpsertPrStatus(string prUrl, string owner, string repo, string status, string branch, DateTime lastChecked)
        {
        }

        public List<string> GetNonMergedPrUrls()
        {
            return [];
        }

        public long GetDatabaseSize()
        {
            return 0;
        }

        public DateTime GetLastSyncTime()
        {
            return DateTime.UtcNow;
        }

        public void SetLastSyncTime(DateTime time)
        {
        }
    }
}

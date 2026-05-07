using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServiceSplitPlanCompletionTests
{
    private static (JobService Service, RecordingPlanReaderService Recorder) CreateService()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var recorder = new RecordingPlanReaderService();
        var service = new JobService(
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10),
            planReaderService: recorder);
        return (service, recorder);
    }

    [Fact]
    public void CompleteJob_SplitPlan_TransitionsToSkipped()
    {
        var (service, recorder) = CreateService();
        var id = service.CreateTestJob("SplitPlan", "/plans/03500-OriginalPlan");

        service.CompleteJob(id, 0);

        Assert.Single(recorder.Transitions);
        Assert.Equal(PlanStatus.Skipped, recorder.Transitions[0].State);
    }

    [Fact]
    public void CompleteJob_UpdatePlan_TransitionsToDraft()
    {
        var (service, recorder) = CreateService();
        var id = service.CreateTestJob("UpdatePlan", "/plans/03501-SomePlan");

        service.CompleteJob(id, 0);

        Assert.Single(recorder.Transitions);
        Assert.Equal(PlanStatus.Draft, recorder.Transitions[0].State);
    }

    [Fact]
    public void CompleteJob_ExpandPlan_TransitionsToDraft()
    {
        var (service, recorder) = CreateService();
        var id = service.CreateTestJob("ExpandPlan", "/plans/03502-SomePlan");

        service.CompleteJob(id, 0);

        Assert.Single(recorder.Transitions);
        Assert.Equal(PlanStatus.Draft, recorder.Transitions[0].State);
    }

    private class RecordingPlanReaderService : IPlanReaderService
    {
        public List<(string FolderName, PlanStatus State)> Transitions { get; } = [];

        public string PlansDirectory => "";
        public bool IsDatabaseReady => true;
#pragma warning disable CS0067
        public event Action? CountsInvalidated;
#pragma warning restore CS0067

        public void TransitionState(string folderName, PlanStatus newState)
        {
            Transitions.Add((folderName, newState));
        }

        public void RecoverStuckPlans() { }
        public void RepairPlans() { }
        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null) => [];
        public PlanFile? GetPlanByFolder(string folderPath) => null;
        public List<PlanFile> GetIceboxPlans() => [];
        public void SaveRevision(string folderName, string content) { }
        public string ReadLatestRevision(string folderName) => "";
        public List<(int Number, string Content, DateTime Modified)> GetRevisions(string folderName) => [];
        public void AddLog(string folderName, string action, string content) { }
        public void DeletePlan(string folderName) { }
        public string ReadRawPlan(string folderName) => "";
        public void SavePlan(string folderName, string fullContent) { }
        public void UpdateLatestRevision(string folderName, string content) { }
        public DashboardModels GetDashboardData(string? projectFilter) => new(0, 0, 0, 0, 0, 0, 0, [], []);
        public decimal GetPlanTotalCost(string folderPath) => 0;
        public int GetPlanTotalTokens(string folderPath) => 0;
        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null) => [];
        public List<Recommendation> GetRecommendations() => [];
        public int GetPendingRecommendationsCount() => 0;
        public PlanReaderService.PlanCountSnapshot ComputePlanCounts() => new(0, 0, 0, 0, 0, 0);
        public void UpdateRecommendationState(string planFolderName, string recommendationTitle, string newState, string? declineReason = null) { }
        public void InvalidateCaches() { }
        public Task FlushPendingWritesAsync() => Task.CompletedTask;
    }
}

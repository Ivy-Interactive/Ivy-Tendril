using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

public interface IPlanReaderService
{
    string PlansDirectory { get; }
    bool IsDatabaseReady { get; }

    void RecoverStuckPlans();
    void RepairPlans();
    List<PlanFile> GetPlans(PlanStatus? statusFilter = null);
    PlanFile? GetPlanByFolder(string folderPath);
    List<PlanFile> GetIceboxPlans();
    void TransitionState(string folderName, PlanStatus newState);
    void ResetToDraft(string folderName);
    void ResetVerificationsForRetry(string folderName);
    void SaveRevision(string folderName, string content);
    void RevertRevision(string folderName);
    string ReadLatestRevision(string folderName);
    List<(int Number, string Content, DateTime Modified)> GetRevisions(string folderName);
    void AddLog(string folderName, string action, string content, string? jobId = null);
    void DeletePlan(string folderName);
    string ReadRawPlan(string folderName);
    void SavePlan(string folderName, string fullContent);
    void UpdateLatestRevision(string folderName, string content);
    DashboardModels GetDashboardData(string? projectFilter);
    decimal GetPlanTotalCost(string folderPath);
    int GetPlanTotalTokens(string folderPath);
    List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null);
    List<Recommendation> GetRecommendations();
    int GetPendingRecommendationsCount();
    PlanReaderService.PlanCountSnapshot ComputePlanCounts();

    void UpdateRecommendationState(string planFolderName, string recommendationTitle, string newState,
        string? declineReason = null);

    List<RecommendationYaml> GetRecommendationsForPlan(string folderName);
    void AcceptRecommendationAndRetry(string folderName, string recommendationTitle);

    void SyncPlanArtifacts(string planFolder);
    PlanFile? GetPlanByFolderFromDisk(string folderPath) => GetPlanByFolder(folderPath);
    void InvalidateCaches();
    Task FlushPendingWritesAsync();

    event Action? CountsInvalidated;
}

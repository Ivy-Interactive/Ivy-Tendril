using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

public interface IPlanReaderService
{
    string PlansDirectory { get; }
    bool IsDatabaseReady { get; }

    void MigratePlans();
    void RecoverStuckPlans();
    List<PlanFile> GetPlans(PlanStatus? statusFilter = null);
    PlanFile? GetPlanByFolder(string folderPath);
    List<PlanFile> GetIceboxPlans();
    void TransitionState(string folderName, PlanStatus newState);

    /// <summary>
    ///     Names of verifications in a Fail state, read from plan.yaml. Empty when the plan is clean.
    ///     A plan must not reach Completed while this is non-empty (see plan 00090).
    /// </summary>
    IReadOnlyList<string> GetFailedVerifications(string folderName);

    /// <summary>
    ///     Overrides the failed-verification block: completes the plan and records
    ///     <see cref="PlanYaml.PartialDelivery" /> so the missing deliverable stays visible downstream.
    /// </summary>
    void CompleteWithPartialDelivery(string folderName);

    /// <summary>
    ///     Returns the reason a plan must not be marked Completed, or null when the transition is fine.
    /// </summary>
    string? GetCompletionBlockReason(string folderName) => null;

    void ResetToDraft(string folderName);
    void ResetVerificationsForRetry(string folderName);
    void SetVerificationStatus(string folderName, string name, VerificationStatus status);
    void SaveRevision(string folderName, string content);
    void RevertRevision(string folderName);
    string ReadLatestRevision(string folderName);
    List<(int Number, string Content, DateTime Modified)> GetRevisions(string folderName);
    void DeletePlan(string folderName);
    string ReadRawPlan(string folderName);
    void SavePlan(string folderName, string fullContent);
    void UpdateLatestRevision(string folderName, string content);
    DashboardModels GetDashboardData(string? projectFilter);
    DashboardActivityStats GetDashboardActivity(int monthsBack = 24);
    List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days);
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
    void AcceptRecommendationsAndRetry(string folderName, IReadOnlyCollection<string> titles);

    void SyncPlanArtifacts(string planFolder);
    PlanFile? GetPlanByFolderFromDisk(string folderPath) => GetPlanByFolder(folderPath);
    void InvalidateCaches();
    Task FlushPendingWritesAsync();

    event Action? CountsInvalidated;
}

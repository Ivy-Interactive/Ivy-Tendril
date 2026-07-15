namespace Ivy.Tendril.Plugins;

public interface ITendrilApi
{
    string StartCreatePlan(string description, string? project = null);
    string StartExecutePlan(string planId, string? note = null);
    TendrilJobStatus? GetJob(string jobId);
    IReadOnlyList<TendrilPlanSummary> ListPlans(string? state = null, string? project = null, int limit = 20);
    IReadOnlyList<string> ListProjects();
}

public record TendrilJobStatus(
    string Id,
    string Type,
    string Status,
    string? StatusMessage,
    string? PlanFile);

public record TendrilPlanSummary(
    string Id,
    string? Title,
    string? State,
    string? Project,
    string? Level);

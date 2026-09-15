namespace Ivy.Tendril.Models;

/// <summary>
///     The group-to-job-type-name table backing <see cref="JobExclusionGroup" />: which
///     <see cref="Helpers.Constants.JobTypes" /> names belong to each group, and what to call the
///     scope in a rejection message.
/// </summary>
public static class JobExclusionGroups
{
    private static readonly Dictionary<JobExclusionGroup, string[]> Members = new()
    {
        [JobExclusionGroup.CreatePlanTask] = [Constants.JobTypes.CreatePlan],
        [JobExclusionGroup.PlanWorktree] =
        [
            Constants.JobTypes.ExecutePlan, Constants.JobTypes.RetryPlan, Constants.JobTypes.CreatePr,
            Constants.JobTypes.UpdatePlan, Constants.JobTypes.ExpandPlan, Constants.JobTypes.SplitPlan
        ],
        [JobExclusionGroup.PlanIssue] = [Constants.JobTypes.CreateIssue],
        [JobExclusionGroup.ProjectSetup] = [Constants.JobTypes.SetupProject]
    };

    public static IReadOnlyList<string> TypesIn(JobExclusionGroup group) =>
        Members.TryGetValue(group, out var types) ? types : [];

    /// <summary>Where the conflict is, for the rejection message.</summary>
    public static string ScopeDescription(JobExclusionGroup group) => group switch
    {
        JobExclusionGroup.PlanWorktree => "on this plan worktree",
        JobExclusionGroup.CreatePlanTask => "for this task",
        JobExclusionGroup.PlanIssue => "on this plan",
        JobExclusionGroup.ProjectSetup => "for this project",
        _ => "for this scope"
    };
}

using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

/// <summary>
///     The single rule deciding whether a plan may reach <see cref="PlanStatus.Completed" />: no
///     verification may be in the Fail state. Shared by <see cref="PlanReaderService" /> (the UI and
///     service paths) and the <c>plan set</c> CLI command so both agree on the answer.
/// </summary>
public static class PlanCompletionGuard
{
    /// <summary>
    ///     Names of the plan's verifications that are in the Fail state, in plan.yaml order. Empty for
    ///     a null plan, so an unreadable plan.yaml never blocks a transition on a guess.
    /// </summary>
    public static IReadOnlyList<string> FailedVerificationNames(PlanYaml? plan) =>
        plan?.Verifications?
            .Where(v => v.Status == VerificationStatus.Fail)
            .Select(v => v.Name)
            .ToList() ?? [];
}

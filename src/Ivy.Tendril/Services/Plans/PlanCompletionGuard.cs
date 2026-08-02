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

    /// <summary>
    ///     Applies a state change to an in-memory <see cref="PlanYaml" />, enforcing the rule above.
    ///     For the write paths that edit plan.yaml directly instead of going through
    ///     <see cref="PlanReaderService.TransitionState" />: the <c>plan set</c> CLI command, the REST
    ///     PUT endpoint and the MCP tool. Caller writes the plan afterwards.
    /// </summary>
    /// <param name="plan">The plan to mutate.</param>
    /// <param name="newState">Target state. Only Completed is guarded.</param>
    /// <param name="allowFailedVerifications">
    ///     When true, a blocked transition proceeds and <see cref="PlanYaml.PartialDelivery" /> is set,
    ///     so an override records the partial delivery instead of silently bypassing the gate.
    /// </param>
    /// <param name="planIdForMessage">Plan ID or folder name, used only in the exception message.</param>
    /// <returns>
    ///     A warning to show the caller when an override was applied, otherwise <c>null</c>.
    /// </returns>
    /// <exception cref="PlanTransitionBlockedException">
    ///     Thrown for a blocked Completed when <paramref name="allowFailedVerifications" /> is false.
    /// </exception>
    public static string? ApplyState(
        PlanYaml plan,
        string newState,
        bool allowFailedVerifications,
        string planIdForMessage)
    {
        string? warning = null;

        if (newState.Equals(nameof(PlanStatus.Completed), StringComparison.OrdinalIgnoreCase))
        {
            var failed = FailedVerificationNames(plan);
            if (failed.Count > 0)
            {
                if (!allowFailedVerifications)
                    throw new PlanTransitionBlockedException(planIdForMessage, failed);

                plan.PartialDelivery = true;
                warning = $"Warning: completing over failed verification(s) {string.Join(", ", failed)}. " +
                          "Marked partialDelivery: true.";
            }
        }

        plan.State = newState;
        return warning;
    }
}

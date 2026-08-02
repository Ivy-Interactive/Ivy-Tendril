using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.Plans;

/// <summary>
///     Thrown by <see cref="PlanReaderService.TransitionState" /> when a requested transition would
///     record work that never happened, e.g. marking a plan Completed after its pre-execution check
///     rejected the plan's premise and it produced no commits and no PRs. Callers are UI actions, so
///     they must catch this and surface <see cref="Message" /> to the user rather than letting the
///     throw disappear into a fire-and-forget handler. See plan 00103.
/// </summary>
public class PlanTransitionBlockedException(string folderName, PlanStatus requestedState, string reason)
    : Exception(reason)
{
    public string FolderName { get; } = folderName;
    public PlanStatus RequestedState { get; } = requestedState;
}

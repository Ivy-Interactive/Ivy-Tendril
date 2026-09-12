namespace Ivy.Tendril.Services;

/// <summary>
///     Indicates where a plan edit originated from.
/// </summary>
public enum PlanEditOrigin
{
    /// <summary>An agent or CLI process, reported through the plan events endpoint.</summary>
    Chat,

    /// <summary>The user, by hand, in the Tendril UI.</summary>
    UserInterface
}

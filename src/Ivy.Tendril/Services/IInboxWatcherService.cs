namespace Ivy.Tendril.Services;

/// <summary>
/// Watches <c>TENDRIL_HOME/Inbox</c> and submits CreatePlan jobs for the files that appear there.
/// Master-only: the inbox is shared state, so a second instance sweeping it turns one dropped file
/// into several jobs.
/// </summary>
public interface IInboxWatcherService : IMasterOnlyStartable, IDisposable
{
}

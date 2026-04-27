namespace Ivy.Tendril.Services;

public interface IPlanWatcherService : IDisposable
{
    /// <summary>
    ///     Raised when plan files change on disk. The string parameter is the changed plan folder
    ///     path (for incremental sync), or null when a full rescan is needed.
    /// </summary>
    event Action<string?>? PlansChanged;

    void NotifyChanged(string? changedPlanFolder = null);
}

internal class NullPlanWatcherService : IPlanWatcherService
{
#pragma warning disable CS0067
    public event Action<string?>? PlansChanged;
#pragma warning restore CS0067
    public void NotifyChanged(string? changedPlanFolder = null) { }
    public void Dispose() { }
}
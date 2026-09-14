namespace Ivy.Tendril.Services.Plans;

public interface IPlanWatcherService : IDisposable
{
    /// <summary>
    ///     Raised when plan files change on disk. The string parameter is the changed plan folder
    ///     path (for incremental sync), or null when a full rescan is needed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two rules bind every subscriber, and #2571 is what happens when one ignores them. A single
    ///         plan mutation can raise this several times, and a burst of plan mutations raises it once per
    ///         plan, so:
    ///     </para>
    ///     <list type="number">
    ///         <item>
    ///             <description>
    ///                 <b>Coalesce.</b> Never do the work inline. Feed a
    ///                 <c>Ivy.Tendril.Hooks.RefreshCoalescer</c> (or an equivalent debounce) so a burst
    ///                 costs one rebuild, not one per event.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <b>Gate, if you render one plan.</b> A subscriber showing a single plan must drop
    ///                 events naming a different plan, via
    ///                 <c>Ivy.Tendril.Hooks.PlanRefreshGate.ShouldRefreshFor</c>. A subscriber whose work
    ///                 spans all plans (a list, a count, a full sync) is folder-blind by design and must
    ///                 not gate - see <c>Ivy.Tendril.Hooks.UseInboxAutoRefreshExtensions.UseInboxAutoRefresh</c>.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <c>PlansChangedSubscriberAuditTests</c> fails when a subscriber is added without being
    ///         audited against both rules.
    ///     </para>
    /// </remarks>
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

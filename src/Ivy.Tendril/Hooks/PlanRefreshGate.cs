namespace Ivy.Tendril.Hooks;

/// <summary>
///     The folder gate every view that renders a <em>single</em> plan must put in front of
///     <see cref="Services.Plans.IPlanWatcherService.PlansChanged" />. Lives here rather than in one
///     app so the next subscriber can reuse it instead of rediscovering it (#2571).
/// </summary>
/// <remarks>
///     List views are the exception and must not use this: see
///     <see cref="UseInboxAutoRefreshExtensions.UseInboxAutoRefresh" />.
/// </remarks>
internal static class PlanRefreshGate
{
    /// <summary>
    ///     Whether a <c>PlansChanged</c> naming <paramref name="changedFolder" /> should rebuild the
    ///     content shown for <paramref name="selectedFolder" />. A rebuild runs git subprocesses and one
    ///     PowerShell condition per configured review action, so doing it because some other plan changed
    ///     is pure cost (#2571). A null changed folder is a full rescan and always refreshes, as does a
    ///     view with nothing selected, which has nothing to compare against.
    /// </summary>
    /// <remarks>
    ///     The event carries a full folder path from the watcher and a bare folder name from some
    ///     <c>NotifyChanged</c> callers, so the two sides are compared on their last segment.
    /// </remarks>
    internal static bool ShouldRefreshFor(string? changedFolder, string? selectedFolder)
    {
        if (string.IsNullOrEmpty(changedFolder) || string.IsNullOrEmpty(selectedFolder)) return true;
        return string.Equals(LeafName(changedFolder), LeafName(selectedFolder), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The last path segment, whichever separator the caller used.</summary>
    private static string LeafName(string folder)
    {
        var trimmed = folder.TrimEnd('/', '\\');
        var lastSeparator = trimmed.LastIndexOfAny(['/', '\\']);
        return lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;
    }
}

using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.AppShell;

/// <summary>
///     The contextual list an app shows in the shell sidebar (plans for Review/Drafts,
///     recommendations, ...). The active app publishes this on every build; the shell
///     renders it and routes item clicks back through <see cref="BuildSelectArgs"/> as
///     a normal navigation to <see cref="AppId"/>. Delegates are fine here — signals
///     never leave the process.
/// </summary>
/// <param name="OnSearch">
///     What the section's search icon does for this list; null means the plan search dialog,
///     which is right for every plan list and wrong for anything else.
/// </param>
/// <param name="SearchLabel">The search icon's tooltip, e.g. "Search chats"; null reads "Search plans".</param>
public record ShellSidebarListState(
    string AppId,
    string Title,
    List<ShellSectionItemDto> Items,
    string? SelectedId,
    Func<string, object?> BuildSelectArgs,
    bool Searchable = true,
    Action? OnSearch = null,
    string? SearchLabel = null);

[Signal(BroadcastType.AppShell)]
public class ShellSidebarListSignal : AbstractSignal<ShellSidebarListState, Unit> { }

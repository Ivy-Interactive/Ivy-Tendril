using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.AppShell;

/// <summary>
///     The contextual list an app shows in the shell sidebar (plans for Review/Drafts,
///     recommendations, ...). The active app publishes this on every build; the shell
///     renders it and routes item clicks back through <see cref="BuildSelectArgs"/> as
///     a normal navigation to <see cref="AppId"/>. Delegates are fine here — signals
///     never leave the process.
/// </summary>
public record ShellSidebarListState(
    string AppId,
    string Title,
    List<ShellSectionItemDto> Items,
    string? SelectedId,
    Func<string, object?> BuildSelectArgs,
    bool Searchable = true,
    Action<string, string>? OnItemAction = null);

[Signal(BroadcastType.AppShell)]
public class ShellSidebarListSignal : AbstractSignal<ShellSidebarListState, Unit> { }

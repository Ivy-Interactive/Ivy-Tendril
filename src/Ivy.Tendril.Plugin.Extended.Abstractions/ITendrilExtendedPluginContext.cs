using Ivy.Plugins;

namespace Ivy.Tendril.Plugins;

/// <summary>
/// Extended plugin context for Tendril plugins that need to contribute
/// settings menu items to the footer menu.
/// </summary>
public interface ITendrilExtendedPluginContext : IIvyExtendedPluginContext, ITendrilPluginContext
{
    /// <summary>
    /// Adds a menu item to the footer settings menu at the specified placement.
    /// The item's Tag property must be set and will be used for stable sorting
    /// within the same position bucket.
    /// </summary>
    void AddSettingsMenuItem(MenuItem item, MenuPlacement placement);

    /// <summary>
    /// Registers a dialog factory and returns an Action that, when invoked, opens it.
    /// The factory receives an IState&lt;bool&gt; controlling the dialog's open/close state.
    /// </summary>
    Action RegisterDialog(string id, Func<IState<bool>, object?> factory);
}

using Ivy.Plugins;

namespace Ivy.Tendril.Plugins;

/// <summary>
/// Extended plugin context for Tendril plugins that need to contribute
/// settings menu items to the footer menu.
/// </summary>
public interface ITendrilExtendedPluginContext : IIvyExtendedPluginContext, ITendrilPluginContext
{
    /// <summary>
    /// Adds a transformer that modifies the settings menu items.
    /// Transformers are applied in priority order (lower = first).
    /// </summary>
    void TransformSettingsMenuItems(Func<IEnumerable<MenuItem>, IEnumerable<MenuItem>> transformer, int priority = 0);

    /// <summary>
    /// Adds a badge provider for the given menu tag. The count function is called
    /// during menu rendering to display notification badges on sidebar items.
    /// </summary>
    void AddBadgeProvider(string menuTag, Func<IServiceProvider, int> countProvider);

    /// <summary>
    /// Registers a dialog factory and returns an Action that, when invoked, opens it.
    /// The factory receives an IState&lt;bool&gt; controlling the dialog's open/close state.
    /// </summary>
    Action RegisterDialog(string id, Func<IState<bool>, object?> factory);
}

using Ivy.Plugins;

namespace Ivy.Tendril.Plugins;

/// <summary>
/// Extended plugin context for Tendril plugins that need to contribute
/// settings menu items to the footer menu.
/// </summary>
public interface ITendrilExtendedPluginContext : IIvyExtendedPluginContext
{
    /// <summary>
    /// Adds a menu item to the footer settings menu at the specified position.
    /// The item's Tag property must be set and will be used for stable sorting
    /// within the same position bucket.
    /// </summary>
    void AddSettingsMenuItem(MenuItem item, FooterMenuPosition position);
}

namespace Ivy.Tendril.Plugins;

/// <summary>
/// Specifies where a plugin-contributed settings menu item should be placed
/// relative to the built-in footer menu items.
/// </summary>
public enum FooterMenuPosition
{
    /// <summary>
    /// Insert at the top of the settings menu, before all built-in items.
    /// </summary>
    Top,

    /// <summary>
    /// Insert at the bottom of the settings menu, after all built-in items.
    /// </summary>
    Bottom,

    /// <summary>
    /// Insert after the "Import Issues from GitHub" menu item.
    /// </summary>
    ImportIssues,
}

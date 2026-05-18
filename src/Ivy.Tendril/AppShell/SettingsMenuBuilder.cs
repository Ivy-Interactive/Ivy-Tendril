using Ivy.Tendril.Plugins;

namespace Ivy.Tendril.AppShell;

internal static class SettingsMenuBuilder
{
    /// <summary>
    /// Builds the final settings menu by inserting plugin-contributed items
    /// into the appropriate positions among the built-in items.
    /// Items within each position bucket are sorted by Tag alphabetically.
    /// </summary>
    public static MenuItem[] Build(
        MenuItem[] builtInItems,
        IReadOnlyList<(MenuItem Item, FooterMenuPosition Position)> pluginItems)
    {
        if (pluginItems.Count == 0)
            return builtInItems;

        var topItems = pluginItems
            .Where(x => x.Position == FooterMenuPosition.Top)
            .OrderBy(x => (string?)x.Item.Tag, StringComparer.Ordinal)
            .Select(x => x.Item);

        var bottomItems = pluginItems
            .Where(x => x.Position == FooterMenuPosition.Bottom)
            .OrderBy(x => (string?)x.Item.Tag, StringComparer.Ordinal)
            .Select(x => x.Item);

        var afterImportIssues = pluginItems
            .Where(x => x.Position == FooterMenuPosition.ImportIssues)
            .OrderBy(x => (string?)x.Item.Tag, StringComparer.Ordinal)
            .Select(x => x.Item);

        var result = new List<MenuItem>();

        // Top items first
        result.AddRange(topItems);

        // Built-in items with insertions after specific tags
        foreach (var item in builtInItems)
        {
            result.Add(item);

            if ((string?)item.Tag == "$import-issues")
                result.AddRange(afterImportIssues);
        }

        // Bottom items last
        result.AddRange(bottomItems);

        return result.ToArray();
    }
}

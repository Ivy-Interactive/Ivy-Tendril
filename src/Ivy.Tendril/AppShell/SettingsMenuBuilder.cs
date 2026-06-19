using Ivy.Tendril.Plugins;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.AppShell;

internal static class SettingsMenuBuilder
{
    /// <summary>
    /// Builds the final settings menu by inserting plugin-contributed items
    /// into positions relative to built-in items or other plugin items.
    /// Placement is resolved at build time so plugin load order is irrelevant.
    /// </summary>
    public static MenuItem[] Build(
        MenuItem[] builtInItems,
        IReadOnlyList<(MenuItem Item, MenuPlacement Placement)> pluginItems,
        ILogger? logger = null)
    {
        if (pluginItems.Count == 0)
            return builtInItems;

        // Start with built-in items as the base sequence
        var result = new List<MenuItem>(builtInItems);

        // Separate plugin items by position type
        var topItems = pluginItems
            .Where(x => x.Placement.Position == MenuPosition.Top)
            .OrderBy(x => x.Placement.Priority)
            .ThenBy(x => (string?)x.Item.Tag, StringComparer.Ordinal)
            .Select(x => x.Item)
            .ToList();

        var bottomItems = pluginItems
            .Where(x => x.Placement.Position == MenuPosition.Bottom)
            .OrderBy(x => x.Placement.Priority)
            .ThenBy(x => (string?)x.Item.Tag, StringComparer.Ordinal)
            .Select(x => x.Item)
            .ToList();

        var anchoredItems = pluginItems
            .Where(x => x.Placement.Position is MenuPosition.After or MenuPosition.Before)
            .OrderBy(x => x.Placement.Priority)
            .ThenBy(x => (string?)x.Item.Tag, StringComparer.Ordinal)
            .ToList();

        // Insert top items at the beginning
        result.InsertRange(0, topItems);

        // Insert anchored items relative to their target tags.
        // We process them iteratively so plugins can anchor to other plugin items.
        var unresolved = new List<(MenuItem Item, MenuPlacement Placement)>(anchoredItems);
        var maxPasses = unresolved.Count + 1; // prevent infinite loops if tags are missing

        for (var pass = 0; pass < maxPasses && unresolved.Count > 0; pass++)
        {
            var stillUnresolved = new List<(MenuItem Item, MenuPlacement Placement)>();

            foreach (var (item, placement) in unresolved)
            {
                var anchorIndex = result.FindIndex(m => (string?)m.Tag == placement.AnchorTag);
                if (anchorIndex < 0)
                {
                    stillUnresolved.Add((item, placement));
                    continue;
                }

                var insertIndex = placement.Position == MenuPosition.After
                    ? anchorIndex + 1
                    : anchorIndex;

                result.Insert(insertIndex, item);
            }

            if (stillUnresolved.Count == unresolved.Count)
                break; // no progress — remaining anchors are unresolvable

            unresolved = stillUnresolved;
        }

        // Any items with unresolvable anchors fall back to bottom
        foreach (var (item, placement) in unresolved)
        {
            logger?.LogWarning(
                "Menu item '{Tag}' references anchor '{Anchor}' which does not exist. Placing at bottom.",
                (string?)item.Tag, placement.AnchorTag);
            bottomItems.Add(item);
        }

        // Append bottom items at the end
        result.AddRange(bottomItems);

        return result.ToArray();
    }
}

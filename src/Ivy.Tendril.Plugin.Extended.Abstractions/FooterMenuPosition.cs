namespace Ivy.Tendril.Plugins;

/// <summary>
/// Specifies where a plugin-contributed settings menu item should be placed.
/// Use the static factory methods to create placements.
/// </summary>
public sealed record MenuPlacement
{
    /// <summary>Place at the top of the menu. Lower priority = closer to the top.</summary>
    public static MenuPlacement Top(int priority = 0) => new() { Position = MenuPosition.Top, Priority = priority };

    /// <summary>Place at the bottom of the menu. Lower priority = closer to the last built-in item.</summary>
    public static MenuPlacement Bottom(int priority = 0) => new() { Position = MenuPosition.Bottom, Priority = priority };

    /// <summary>Place after the item with the given tag. Lower priority = closer to the anchor.</summary>
    public static MenuPlacement After(string tag, int priority = 0) => new() { Position = MenuPosition.After, AnchorTag = tag, Priority = priority };

    /// <summary>Place before the item with the given tag. Lower priority = closer to the anchor.</summary>
    public static MenuPlacement Before(string tag, int priority = 0) => new() { Position = MenuPosition.Before, AnchorTag = tag, Priority = priority };

    public MenuPosition Position { get; init; }
    public string? AnchorTag { get; init; }
    public int Priority { get; init; }
}

public enum MenuPosition { Top, Bottom, After, Before }

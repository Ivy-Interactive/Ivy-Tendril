namespace Ivy.Tendril.Helpers;

/// <summary>
/// Helpers for responsive action bars with progressive button collapse.
///
/// Breakpoints resolve against the APP CONTENT CONTAINER, not the raw viewport: the
/// framework's AppHostWidget mounts a container-scoped BreakpointProvider, so an expanded
/// app sidebar already narrows the measured width (see use-responsive.ts). Bands:
/// Mobile &lt;640, Tablet 640–767, Desktop 768–1023, Wide ≥1024 (unbounded above).
///
/// A tier can still never GUARANTEE its inline set fits: inner list panes (e.g. the
/// Draft/Review SidebarLayout) eat into the container, and sidebars are user-resizable
/// (200–600px). Always pair these tiers with <c>.Wrap()</c> on the bar — FooterLayout's
/// footer slot grows with its content, so a wrapped second row is the safe fallback where
/// a single row would clip (issue #1433).
/// </summary>
public static class ActionBarResponsive
{
    /// <summary>
    /// Shows button at all breakpoints (always visible). Used for navigation
    /// (Previous/Next) which must stay visible at every size.
    /// </summary>
    public static Button AlwaysVisible(this Button btn)
    {
        return btn;
    }

    /// <summary>
    /// Shows button at the Compact tier and up (container ≥768px, i.e. Desktop + Wide).
    /// Hidden at the Minimal tier, where it collapses into a dropdown.
    /// </summary>
    public static Button CompactUp(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Desktop, Breakpoint.Wide);
    }

    /// <summary>
    /// Shows button only at the Full tier (container ≥1024px, Wide). At narrower tiers it
    /// collapses into a dropdown.
    /// </summary>
    public static Button FullOnly(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Wide);
    }

    /// <summary>
    /// Shows button at every tier except Full (container &lt;1024px). Complement of
    /// <see cref="FullOnly"/> — used for a shortcut-carrying stand-in that must stay
    /// mounted while its labeled Full-tier counterpart is unmounted (ShowOn/HideOn truly
    /// unmount, which deregisters a Button's ShortcutKey; MenuItem shortcuts are
    /// display-only).
    /// </summary>
    public static Button BelowFull(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Mobile, Breakpoint.Tablet, Breakpoint.Desktop);
    }

    /// <summary>
    /// Dropdown visible only at the Full tier (Wide). Holds the standard overflow items.
    /// </summary>
    public static DropDownMenu DropdownAtFull(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Wide);
    }

    /// <summary>
    /// Dropdown visible only at the Compact tier (Desktop, 768–1023px). Holds the buttons
    /// not shown inline at this tier plus the standard overflow items.
    /// </summary>
    public static DropDownMenu DropdownAtCompact(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Desktop);
    }

    /// <summary>
    /// Dropdown visible only at the Minimal tier (container &lt;768px, Mobile + Tablet).
    /// Holds every action button not always-visible plus the standard overflow items.
    /// </summary>
    public static DropDownMenu DropdownAtMinimal(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);
    }

    /// <summary>
    /// Dropdown visible at every tier except Full (container &lt;1024px). Pairs with
    /// <see cref="FullOnly"/> buttons for bars whose entire item set collapses into one
    /// dropdown below the Full tier.
    /// </summary>
    public static DropDownMenu DropdownBelowWide(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Mobile, Breakpoint.Tablet, Breakpoint.Desktop);
    }
}

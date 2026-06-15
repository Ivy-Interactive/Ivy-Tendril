namespace Ivy.Tendril.Helpers;

/// <summary>
/// Helper for creating responsive action bars with progressive button collapse.
/// Buttons collapse into dropdown menus as screen size decreases.
/// </summary>
public static class ActionBarResponsive
{
    /// <summary>
    /// Shows button only on Desktop breakpoint and above (>=768px).
    /// Hidden on Mobile and Tablet.
    /// </summary>
    public static Button DesktopUp(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Desktop);
    }

    /// <summary>
    /// Shows button at all breakpoints (always visible).
    /// </summary>
    public static Button AlwaysVisible(this Button btn)
    {
        return btn;
    }

    /// <summary>
    /// Shows button only below Desktop breakpoint (&lt;768px).
    /// Visible on Mobile and Tablet only.
    /// </summary>
    public static Button BelowDesktop(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);
    }

    /// <summary>
    /// Creates a dropdown menu that is visible only on Desktop and above (>=768px).
    /// Hidden on Mobile and Tablet.
    /// </summary>
    public static Button DropdownAtDesktop(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Desktop);
    }

    /// <summary>
    /// Creates a dropdown menu that is visible only below Desktop (&lt;768px).
    /// Visible on Mobile and Tablet only.
    /// </summary>
    public static Button DropdownAtMobile(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);
    }
}

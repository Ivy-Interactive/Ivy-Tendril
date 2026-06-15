namespace Ivy.Tendril.Helpers;

/// <summary>
/// Helper for creating responsive action bars with progressive button collapse.
/// Buttons collapse into dropdown menus as screen size decreases.
///
/// IMPORTANT — breakpoint bands (see <see cref="Breakpoint"/> and the frontend
/// <c>use-responsive.ts</c>): a breakpoint name maps to a width *band*, not a
/// "this width and up" range:
/// <list type="bullet">
///   <item><c>Mobile</c>  → width &lt; 640px</item>
///   <item><c>Tablet</c>  → 640px ≤ width &lt; 768px</item>
///   <item><c>Desktop</c> → 768px ≤ width &lt; 1024px</item>
///   <item><c>Wide</c>    → width ≥ 1024px</item>
/// </list>
/// A normal/large desktop monitor (≥1024px) therefore resolves to <c>Wide</c>, NOT
/// <c>Desktop</c>. The three collapse tiers used by the action bars map to bands as:
/// <list type="bullet">
///   <item><b>Full</b> tier (everything visible) → <c>Wide</c></item>
///   <item><b>Compact</b> tier (some buttons collapsed) → <c>Desktop</c></item>
///   <item><b>Minimal</b> tier (most buttons collapsed) → <c>Mobile</c> + <c>Tablet</c></item>
/// </list>
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
    /// Shows button only at the Full tier — a wide desktop monitor (width ≥1024px).
    /// Hidden at the Compact and Minimal tiers, where the button collapses into a dropdown.
    /// </summary>
    public static Button FullOnly(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Wide);
    }

    /// <summary>
    /// Shows button at the Compact tier and up — a wide monitor or the 768–1023px band
    /// (<c>Desktop</c> + <c>Wide</c>). Hidden only at the Minimal tier (&lt;768px), where
    /// the button collapses into a dropdown.
    /// </summary>
    public static Button CompactUp(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Desktop, Breakpoint.Wide);
    }

    /// <summary>
    /// Creates a dropdown menu visible only at the Full tier (width ≥1024px). Holds the
    /// standard overflow items when all action buttons are shown inline.
    /// </summary>
    public static DropDownMenu DropdownAtFull(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Wide);
    }

    /// <summary>
    /// Creates a dropdown menu visible only at the Compact tier (768–1023px band).
    /// Holds the buttons collapsed at this tier plus the standard overflow items.
    /// </summary>
    public static DropDownMenu DropdownAtCompact(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Desktop);
    }

    /// <summary>
    /// Creates a dropdown menu visible only at the Minimal tier (width &lt;768px,
    /// i.e. <c>Mobile</c> + <c>Tablet</c>). Holds the buttons collapsed at this tier
    /// plus the standard overflow items.
    /// </summary>
    public static DropDownMenu DropdownAtMinimal(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);
    }
}

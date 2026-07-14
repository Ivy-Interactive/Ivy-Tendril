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
/// These bands are measured against the browser VIEWPORT, not the element's own
/// rendered width. The Draft and Review footers, however, live in the CONTENT PANE,
/// which is narrower than the viewport by roughly the app sidebar (~420px) plus —
/// for these two apps specifically — a list pane (~350px) to its left. A ~1456px-wide
/// viewport (comfortably <c>Wide</c>) can therefore leave a content pane only as wide
/// as a much narrower viewport would give a full-width layout, so a naive "show
/// everything at Wide" tier still clips (see issue #1433).
///
/// To account for this, the footer helpers below shift the button budget down by one
/// viewport tier from what the numbers alone would suggest: a set that would safely
/// fit a full-width element at <c>Desktop</c> is only shown inline once the viewport
/// reaches <c>Wide</c>, and there is no viewport tier at which the widest, Full-tier-only
/// button set is safe — those items are always reachable via the dropdown instead of
/// ever being guaranteed inline. Do not "simplify" this back to the naive
/// viewport-only mapping; it will reproduce the clipping bug.
/// <list type="bullet">
///   <item><b>Pane-Compact</b> tier (Previous/Next + a couple of primary actions inline) → <c>Wide</c> only</item>
///   <item><b>Pane-Minimal</b> tier (only Previous/Next inline) → <c>Mobile</c> + <c>Tablet</c> + <c>Desktop</c></item>
/// </list>
///
/// The project-configured review-actions row (<see cref="Apps.Review.ReviewActionsBarView"/>)
/// has an unbounded, arbitrary-length item list, so even the Wide tier caps how many
/// render inline and keeps the dropdown reachable at every breakpoint, including Wide.
///
/// <see cref="CompactUp"/>/<see cref="DropdownAtFull"/>/<see cref="DropdownAtCompact"/>/
/// <see cref="DropdownAtMinimal"/> below are the plain, viewport-only tiers (no pane-width
/// adjustment), kept only because <c>Apps.Recommendations.ContentView</c> still uses them.
/// NOTE: Recommendations sits behind the same app-sidebar + list-pane geometry as Draft/Review
/// (see <c>RecommendationsApp</c>'s <c>SidebarLayout</c> composition), so it likely has the
/// same Wide-tier clipping risk as issue #1433 — it just wasn't in that issue's repro and is
/// out of scope for this fix. Prefer the Pane-* helpers below for any bar in a SidebarLayout
/// content pane; the plain viewport-only helpers are not proven safe for that geometry.
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
    /// Shows button only at the Wide tier (viewport width ≥1024px). Hidden at every other
    /// breakpoint, where the button collapses into a dropdown. Used for rows built from an
    /// arbitrary, unbounded item count (e.g. project-configured actions), where fixed
    /// button-count tiers can't safely guarantee everything fits below Wide.
    /// </summary>
    public static Button FullOnly(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Wide);
    }

    /// <summary>
    /// Creates a dropdown menu visible at every breakpoint except Wide (i.e. <c>Mobile</c> +
    /// <c>Tablet</c> + <c>Desktop</c>, width &lt;1024px). Pairs with <see cref="FullOnly"/> for
    /// rows with an arbitrary, unbounded item count: buttons are shown inline only on a wide
    /// desktop monitor, everything else collapses into this single dropdown.
    /// </summary>
    public static DropDownMenu DropdownBelowWide(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Mobile, Breakpoint.Tablet, Breakpoint.Desktop);
    }

    /// <summary>
    /// Shows button only once the viewport reaches the Pane-Compact tier (<c>Wide</c>,
    /// ≥1024px). Used for the Draft/Review footers, whose CONTENT PANE is narrower than
    /// the viewport by the sidebar + list pane to its left (see the pane-width note on
    /// this class); a button set that would fit a full-width element at <c>Desktop</c>
    /// only fits the pane once the viewport itself reaches <c>Wide</c>.
    /// </summary>
    public static Button PaneCompactUp(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Wide);
    }

    /// <summary>
    /// Creates a dropdown menu visible only at the Pane-Compact tier (<c>Wide</c>,
    /// ≥1024px). Holds the buttons not shown inline at this tier plus the standard
    /// overflow items.
    /// </summary>
    public static DropDownMenu DropdownAtPaneCompact(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Wide);
    }

    /// <summary>
    /// Creates a dropdown menu visible at the Pane-Minimal tier (viewport width &lt;1024px,
    /// i.e. <c>Mobile</c> + <c>Tablet</c> + <c>Desktop</c>). Holds every action button not
    /// always-visible plus the standard overflow items — nothing beyond Previous/Next fits
    /// the content pane below the Wide viewport tier.
    /// </summary>
    public static DropDownMenu DropdownAtPaneMinimal(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Mobile, Breakpoint.Tablet, Breakpoint.Desktop);
    }

    /// <summary>
    /// Plain viewport-only tier (no pane-width adjustment): shows button at the Compact tier
    /// and up — a wide monitor or the 768–1023px band (<c>Desktop</c> + <c>Wide</c>). Hidden
    /// only at the Minimal tier (&lt;768px). Kept for <c>Apps.Recommendations.ContentView</c>;
    /// see the pane-width caveat on this class before reusing it elsewhere.
    /// </summary>
    public static Button CompactUp(this Button btn)
    {
        return btn.ShowOn(Breakpoint.Desktop, Breakpoint.Wide);
    }

    /// <summary>
    /// Plain viewport-only tier: dropdown visible only at the Full/Wide tier (width ≥1024px).
    /// Kept for <c>Apps.Recommendations.ContentView</c>; see the pane-width caveat on this
    /// class before reusing it elsewhere.
    /// </summary>
    public static DropDownMenu DropdownAtFull(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Wide);
    }

    /// <summary>
    /// Plain viewport-only tier: dropdown visible only at the Compact tier (768–1023px band).
    /// Kept for <c>Apps.Recommendations.ContentView</c>; see the pane-width caveat on this
    /// class before reusing it elsewhere.
    /// </summary>
    public static DropDownMenu DropdownAtCompact(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Desktop);
    }

    /// <summary>
    /// Plain viewport-only tier: dropdown visible only at the Minimal tier (width &lt;768px,
    /// i.e. <c>Mobile</c> + <c>Tablet</c>). Kept for <c>Apps.Recommendations.ContentView</c>;
    /// see the pane-width caveat on this class before reusing it elsewhere.
    /// </summary>
    public static DropDownMenu DropdownAtMinimal(Button trigger, params MenuItem[] items)
    {
        return trigger.WithDropDown(items).ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);
    }
}

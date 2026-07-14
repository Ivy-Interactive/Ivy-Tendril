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
/// rendered width. The Draft, Review, and Recommendations footers, however, live in the
/// CONTENT PANE, which is narrower than the viewport by roughly the app sidebar (~420px)
/// plus — for these apps specifically — a list pane (~350px) to its left. A ~1456px-wide
/// viewport (comfortably <c>Wide</c>) can therefore leave a content pane only as wide
/// as a much narrower viewport would give a full-width layout, so a naive "show
/// everything at Wide" tier still clips (see issue #1433).
///
/// To account for this, the footer helpers below shift the button budget down by one
/// viewport tier from what the numbers alone would suggest: a set that would fit a
/// full-width element at <c>Desktop</c> is only shown inline once the viewport reaches
/// <c>Wide</c>, and there is no viewport tier at which the widest, Full-tier-only button
/// set is safe — those items are always reachable via the dropdown instead of ever being
/// guaranteed inline. This still isn't watertight: <c>Wide</c> is unbounded above, so a
/// pane at the narrow end of that band (roughly a 1024–1300px viewport, well short of the
/// #1433 repro's ~1456px) can still clip the Pane-Compact-tier inline set — there's no
/// lower tier to fall back to once <c>Wide</c> is reached. Do not "simplify" this back to
/// the naive viewport-only mapping; it will reproduce the clipping bug, and don't read the
/// Wide tier as a guarantee either.
/// <list type="bullet">
///   <item><b>Pane-Compact</b> tier (Previous/Next + a couple of primary actions inline) → <c>Wide</c> only</item>
///   <item><b>Pane-Minimal</b> tier (only Previous/Next inline) → <c>Mobile</c> + <c>Tablet</c> + <c>Desktop</c></item>
/// </list>
///
/// The project-configured review-actions row (<see cref="Apps.Review.ReviewActionsBarView"/>)
/// has an unbounded, arbitrary-length item list, so even the Wide tier caps how many
/// render inline and keeps the dropdown reachable at every breakpoint, including Wide.
/// That row is also the last caller of the plain, viewport-only <see cref="FullOnly"/> /
/// <see cref="DropdownBelowWide"/> pair below — its item count is unbounded, so it can't
/// use fixed button-count Pane-* tiers the way Drafts/Review/Recommendations footers do.
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
    /// ≥1024px). Used for the Draft/Review/Recommendations footers, whose CONTENT PANE is
    /// narrower than the viewport by the sidebar + list pane to its left (see the
    /// pane-width note on this class); a button set that would fit a full-width element
    /// at <c>Desktop</c> only fits the pane once the viewport itself reaches <c>Wide</c>.
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
}

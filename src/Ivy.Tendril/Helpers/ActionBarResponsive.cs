using Ivy;

namespace Ivy.Tendril.Helpers;

/// <summary>
/// Responsive breakpoints for plan/review action bars.
/// >= 1024 (Wide): all buttons. 768–1023 (Desktop): four icon + kbd + …. &lt; 768: prev/next + ….
/// </summary>
public static class ActionBarResponsive
{
    // Ivy renders <kbd> only when Title is set; nbsp hides the label on compact tiers.
    private const string HiddenTitle = "\u00A0";

    private static readonly Responsive<bool?> VisibleAtWide = new() { Default = false, Desktop = false, Wide = true };
    private static readonly Responsive<bool?> DesktopOnly = new() { Default = false, Desktop = true, Wide = false };
    private static readonly Responsive<bool?> BelowTablet = new() { Default = true, Desktop = false };

    public static Fragment AtWide(Button template) =>
        new(template with { ResponsiveVisible = VisibleAtWide });

    public static Fragment WideAndDesktopCompact(Button template)
    {
        var label = template.Title;
        return new Fragment(
            template with { Title = label, ResponsiveVisible = VisibleAtWide },
            template with { Title = HiddenTitle, ResponsiveVisible = DesktopOnly });
    }

    public static Fragment WideDesktopAndMobileNav(Button template)
    {
        var label = template.Title;
        return new Fragment(
            template with { Title = label, ResponsiveVisible = VisibleAtWide },
            template with { Title = HiddenTitle, ResponsiveVisible = DesktopOnly },
            template with { Title = HiddenTitle, ResponsiveVisible = BelowTablet });
    }

    public static DropDownMenu BelowTabletMenu(Button trigger, params MenuItem[] items) =>
        trigger.WithDropDown(items) with { ResponsiveVisible = BelowTablet };

    public static DropDownMenu DesktopOnlyMenu(Button trigger, params MenuItem[] items) =>
        trigger.WithDropDown(items) with { ResponsiveVisible = DesktopOnly };

    public static DropDownMenu WideOnlyMenu(Button trigger, params MenuItem[] items) =>
        trigger.WithDropDown(items) with { ResponsiveVisible = VisibleAtWide };
}

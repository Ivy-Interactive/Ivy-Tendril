namespace Ivy.Tendril.Apps.Views;

/// <summary>
/// Builds the shared app content header used by the Recommendation, Draft, and Review apps.
/// On desktop the title and the controls fit on a single row (title left, controls right).
/// When the row runs out of horizontal room — e.g. on mobile — the controls (action buttons +
/// counter) wrap onto a second row below the title + issue-link button.
/// </summary>
public static class ResponsiveHeader
{
    /// <summary>
    /// Composes <paramref name="titleArea"/> (title text, issue/PR link, badges, mobile picker)
    /// and <paramref name="controls"/> (counter + primary action buttons) into a header that
    /// keeps both on one row when they fit and wraps the controls below the title when they
    /// do not.
    /// </summary>
    /// <remarks>
    /// We use <c>.Wrap()</c> rather than a responsive <c>Orientation</c>: the wire format
    /// serializes a responsive <c>Orientation</c> onto the plain <c>orientation</c> prop, which
    /// the StackLayout frontend reads as a single value and so never resolves per breakpoint
    /// (the <c>responsiveOrientation</c> prop is never populated). <c>Wrap</c> is a plain boolean
    /// the frontend honours at every breakpoint. <paramref name="titleArea"/> grows to fill the
    /// first row, pushing <paramref name="controls"/> — itself one atomic horizontal block — onto
    /// the next row as a unit. No fixed height: a single row is content-height, and a wrapped
    /// header is free to grow taller.
    /// </remarks>
    public static object Build(object titleArea, object controls)
    {
        return Layout.Horizontal()
                   .Wrap()
                   .Width(Size.Full())
                   .Gap(2)
                   .AlignContent(Align.Left)
               | titleArea
               | controls;
    }
}

namespace Ivy.Tendril.Apps.Views;

/// <summary>
/// Builds the shared app content header used by the Recommendation, Draft, and Review apps.
/// On desktop the title and the controls sit on a single 40px row (title left, controls right).
/// On mobile/tablet the header stacks: the title + issue-link button on the first row and the
/// action buttons + counter on a row below.
/// </summary>
public static class ResponsiveHeader
{
    /// <summary>
    /// Composes the title area (title text, issue/PR link, badges, mobile picker) and the
    /// controls (counter + primary action buttons) into a breakpoint-aware header.
    /// </summary>
    /// <remarks>
    /// Two layouts are emitted and toggled with <c>ShowOn</c>/<c>HideOn</c> — a horizontal row
    /// for desktop/wide and a vertical stack for mobile/tablet — rather than flipping a single
    /// layout's <c>Orientation</c> responsively. A responsive <c>Orientation</c> serializes onto
    /// the plain <c>orientation</c> prop, which the StackLayout frontend reads as a single value
    /// and never resolves per breakpoint, so it silently renders the same at every width.
    /// Visibility (the prop behind <c>ShowOn</c>/<c>HideOn</c>) is resolved per breakpoint by the
    /// renderer, so it is the reliable primitive here.
    ///
    /// The two layouts can't share widget instances, so the title area and controls are passed as
    /// factories and built once per layout — the same approach the title area itself already uses
    /// to render a desktop title and a mobile picker side by side.
    /// </remarks>
    public static object Build(Func<object> titleArea, Func<object> controls)
    {
        var desktop = new Box(
                Layout.Horizontal().Height(Size.Px(40)).Width(Size.Full()).Gap(2).AlignContent(Align.Left)
                | titleArea()
                | controls())
            .BorderThickness(0).Padding(0).Width(Size.Full())
            .HideOn(Breakpoint.Mobile, Breakpoint.Tablet);

        var mobile = new Box(
                Layout.Vertical().Width(Size.Full()).Gap(2).AlignContent(Align.Left)
                | titleArea()
                | controls())
            .BorderThickness(0).Padding(0).Width(Size.Full())
            .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);

        return new Box(Layout.Vertical().Width(Size.Full()).Gap(0) | desktop | mobile)
            .BorderThickness(0).Padding(0).Width(Size.Full());
    }
}

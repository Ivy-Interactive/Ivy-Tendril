namespace Ivy.Tendril.Apps.Views;

/// <summary>
/// Builds a mobile-only navigation control for the nested master-detail apps. On small screens the
/// list sidebar auto-collapses, so the content header title becomes a dropdown: tapping it shows all
/// items in the list and selecting one switches the detail pane. On larger screens the hosting header
/// should render the plain title instead (apply <c>.HideOn(Breakpoint.Mobile, Breakpoint.Tablet)</c>
/// to the title and <c>.ShowOn(Breakpoint.Mobile, Breakpoint.Tablet)</c> to this picker).
/// </summary>
public static class MobileItemPicker
{
    /// <summary>
    /// Creates the picker dropdown. Returns a <see cref="DropDownMenu"/> so callers can chain
    /// responsive visibility helpers such as <c>.ShowOn(...)</c>.
    /// </summary>
    /// <typeparam name="T">The list item type (e.g. a plan or recommendation).</typeparam>
    public static DropDownMenu Build<T>(
        string currentLabel,
        IReadOnlyList<T> items,
        Func<T, string> labelSelector,
        Func<T, bool> isCurrent,
        Action<T> onSelect)
    {
        // Full-width trigger so the title truncates instead of pushing the header wide. The button
        // and its content row fill 100%; the title text gets Grow() (flex-grow + min-width:0) so the
        // ellipsis engages, and the chevron keeps its natural size pinned to the right.
        var trigger = new Button()
            .Variant(ButtonVariant.Ghost)
            .Width(Size.Full())
            .Content(
                Layout.Horizontal().Gap(1).AlignContent(Align.Left).Width(Size.Full())
                | Text.Block(currentLabel).Bold().NoWrap().Overflow(Overflow.Ellipsis).Width(Size.Grow())
                | Icons.ChevronDown.ToIcon()
            );

        var menuItems = items.Select(item =>
        {
            var captured = item;
            var menuItem = MenuItem.Default(labelSelector(captured))
                .OnSelect(() => onSelect(captured));
            if (isCurrent(captured))
                menuItem = menuItem.Icon(Icons.Check);
            return menuItem;
        }).ToArray();

        return new DropDownMenu(DropDownMenu.DefaultSelectHandler(), trigger, menuItems)
            .Width(Size.Full());
    }
}

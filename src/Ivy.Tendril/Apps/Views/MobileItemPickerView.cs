namespace Ivy.Tendril.Apps.Views;

public static class MobileItemPicker
{
    public static DropDownMenu Build<T>(
        string currentLabel,
        IReadOnlyList<T> items,
        Func<T, string> labelSelector,
        Func<T, bool> isCurrent,
        Action<T> onSelect)
    {
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

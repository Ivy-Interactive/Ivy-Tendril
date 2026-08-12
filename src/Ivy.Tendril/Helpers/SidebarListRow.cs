namespace Ivy.Tendril.Helpers;

public static class SidebarListRow
{
    public static object Build(string title, object content, Action onClick, bool isSelected = false)
    {
        var row = Layout.Vertical().Gap(1)
            | Text.Literal(title)
            | content;

        return BuildButton(row, onClick, isSelected, BorderRadius.None);
    }

    public static object Build(string title, Action onClick, bool isSelected = false)
    {
        return BuildButton(Text.Literal(title), onClick, isSelected, BorderRadius.None);
    }

    public static object Build(string title, Icons icon, Action onClick, bool isSelected = false)
    {
        var row = Layout.Horizontal().Gap(2).AlignContent(Align.Left).Width(Size.Full())
            | icon.ToIcon()
            | Text.Literal(title);

        return BuildButton(row, onClick, isSelected, BorderRadius.Rounded);
    }

    public static object BuildSubItem(string title, Icons? icon, Action onClick, bool isSelected = false)
    {
        var row = Layout.Horizontal().Gap(2).AlignContent(Align.Left).Width(Size.Full())
            | new Spacer().Width(Size.Rem(1));

        if (icon.HasValue)
            row |= icon.Value.ToIcon();

        row |= Text.Literal(title);

        return BuildButton(row, onClick, isSelected, BorderRadius.Rounded);
    }

    // Rows hosted in a List widget sit between its straight separator lines, so they
    // stay square; the icon overload lives in gap-spaced menus and keeps rounding.
    private static Button BuildButton(object content, Action onClick, bool isSelected, BorderRadius radius)
    {
        var button = new Button().Width(Size.Full()).Content(content).OnClick(onClick).BorderRadius(radius);
        return isSelected ? button.Secondary() : button.Ghost();
    }
}

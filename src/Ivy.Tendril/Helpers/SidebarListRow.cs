namespace Ivy.Tendril.Helpers;

public static class SidebarListRow
{
    public static object Build(string title, object content, Action onClick, bool isSelected = false)
    {
        var row = Layout.Vertical()
            | Text.Literal(title)
            | content;

        return BuildButton(row, onClick, isSelected, BorderRadius.None);
    }

    public static object Build(string title, Action onClick, bool isSelected = false)
    {
        return BuildButton(Text.Literal(title), onClick, isSelected, BorderRadius.None);
    }

    public static object Build(string title, Icons icon, Action onClick, bool isSelected = false, int? count = null)
    {
        var row = Layout.Horizontal().AlignContent(Align.Left).Width(Size.Full())
            | icon.ToIcon()
            | Text.Literal(title);

        if (count is > 0)
        {
            row |= new Spacer();
            row |= new Badge(count.Value.ToString()).Variant(BadgeVariant.Secondary).Small();
        }

        return BuildButton(row, onClick, isSelected, BorderRadius.Rounded);
    }

    public static object BuildExpandable(string title, Icons icon, bool isExpanded, Action onClick, bool isSelected = false)
    {
        var row = Layout.Horizontal().AlignContent(Align.Left).Width(Size.Full())
            | icon.ToIcon()
            | Text.Literal(title)
            | new Spacer()
            | (isExpanded ? Icons.ChevronDown : Icons.ChevronRight).ToIcon().Small();

        return BuildButton(row, onClick, isSelected, BorderRadius.Rounded);
    }

    public static object BuildSubItem(string title, Icons? icon, Action onClick, bool isSelected = false)
    {
        return BuildSubItem(title, icon, null, onClick, isSelected);
    }

    public static object BuildSubItem(string title, Icons? icon, Colors? color, Action onClick, bool isSelected = false)
    {
        var row = Layout.Horizontal().AlignContent(Align.Left).Width(Size.Full())
            | new Spacer().Width(Size.Rem(1));

        if (icon.HasValue)
        {
            row |= color.HasValue
                ? new Icon(icon.Value, color.Value).Small()
                : icon.Value.ToIcon();
        }
        else if (color.HasValue)
        {
            row |= new Box()
                .Background(color.Value)
                .BorderRadius(BorderRadius.Rounded)
                .Width(Size.Units(3))
                .Height(Size.Units(3));
        }

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

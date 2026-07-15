namespace Ivy.Tendril.Helpers;

public static class SidebarListRow
{
    public static object Build(string title, object content, Action onClick, bool isSelected = false)
    {
        var row = Layout.Vertical().Gap(1)
            | Text.Literal(title)
            | content;

        return BuildButton(row, onClick, isSelected);
    }

    public static object Build(string title, Action onClick, bool isSelected = false)
    {
        return BuildButton(Text.Literal(title), onClick, isSelected);
    }

    public static object Build(string title, Icons icon, Action onClick, bool isSelected = false)
    {
        var row = Layout.Horizontal().Gap(2).AlignContent(Align.Left).Width(Size.Full())
            | icon.ToIcon()
            | Text.Literal(title);

        return BuildButton(row, onClick, isSelected);
    }

    private static Button BuildButton(object content, Action onClick, bool isSelected)
    {
        var button = new Button().Width(Size.Full()).Content(content).OnClick(onClick);
        return isSelected ? button.Secondary() : button.Ghost();
    }
}

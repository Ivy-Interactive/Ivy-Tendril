namespace Ivy.Tendril.Helpers;

public static class SidebarListRow
{
    public static object Build(string title, object content, Action onClick)
    {
        var row = Layout.Vertical().Gap(1)
            | Text.Literal(title)
            | content;

        return new Button().Ghost().Width(Size.Full()).Content(row).OnClick(onClick);
    }

    public static object Build(string title, Action onClick)
    {
        return new Button().Ghost().Width(Size.Full()).Content(Text.Literal(title)).OnClick(onClick);
    }
}

namespace Ivy.Tendril.Views;

public class NoContentView(string title, string description, object? cta = null) : ViewBase
{
    public override object Build()
    {
        var layout = Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()).Gap(4).Padding(8)
                     | Text.H3(title)
                     | Text.Muted(description);

        if (cta is not null)
            layout |= cta;

        return layout;
    }
}

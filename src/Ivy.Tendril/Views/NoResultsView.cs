namespace Ivy.Tendril.Views;

public class NoResultsView : ViewBase
{
    public override object Build()
    {
        return Layout.Horizontal().Gap(2).AlignContent(Align.TopLeft).Padding(4)
               | new Icon(Icons.SearchX).Color(Colors.Gray)
               | Text.P("No results. Try adjusting your filters.").Small().Color(Colors.Muted);
    }
}

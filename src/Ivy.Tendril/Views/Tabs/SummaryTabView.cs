namespace Ivy.Tendril.Views.Tabs;

public class SummaryTabView(string? summaryMarkdown) : ViewBase
{
    public override object Build()
    {
        if (summaryMarkdown is { } md)
        {
            var layout = Layout.Vertical().Gap(2);
            layout |= new Markdown(md).DangerouslyAllowLocalFiles().Article();
            return layout;
        }

        return Text.Muted("No summary available.");
    }
}

namespace Ivy.Tendril.Apps.Views.Tabs;

public class SummaryTabView(string? summaryMarkdown, bool loading = false) : ViewBase
{
    public override object Build()
    {
        if (summaryMarkdown is { } md)
        {
            var layout = Layout.Vertical().Gap(2);
            layout |= new Markdown(md).DangerouslyAllowLocalFiles().Article();
            return layout;
        }

        if (loading)
            return null!;

        return Text.Muted("No summary available.");
    }
}

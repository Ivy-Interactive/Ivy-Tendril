using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Views.Tabs;

public class SummaryTabView(
    IConfigService config,
    string? summaryMarkdown,
    Action<string>? onLinkClick = null,
    bool loading = false) : ViewBase
{
    public override object Build()
    {
        if (summaryMarkdown is null && loading)
            return null!;

        var md = summaryMarkdown ?? """
                                    # Summary 
                                    > [!NOTE]
                                    > No summary is found for this plan. Please check the verifications for more information.
                                    >
                                    > `Reset to Draft` or `Request Changes` to retry the plan.
                                    """;

        return new PlanMarkdown(MarkdownHelper.PrepareForDisplay(md, config))
            .Article()
            .DangerouslyAllowLocalFiles()
            .Height(Size.Full())
            .OnLinkClick(onLinkClick ?? (_ => { }));
    }
}

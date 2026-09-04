using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Views.Tabs;

public class SummaryTabView(
    IConfigService config,
    string? summaryMarkdown,
    List<PlanVerificationEntry> verifications,
    Dictionary<string, bool> verificationReports,
    Action<string> openVerification,
    Action<string>? onLinkClick = null,
    bool loading = false) : ViewBase
{
    public override object Build()
    {
        if (summaryMarkdown is null && loading)
            return null!;

        // Verifications live only here now, so always render the DraftMarkdown (with a
        // placeholder body when there is no summary) to keep the sticky Verifications box visible.
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
            .StickyContent(BuildVerificationsBox())
            .OnLinkClick(onLinkClick ?? (_ => { }));
    }

    private object BuildVerificationsBox()
    {
        if (verifications.Count == 0)
            return new Card(Text.Muted("No verifications")).Header("Verifications").Width(Size.Px(300));

        var grid = Layout
            .Grid()
            .Columns(2)
            .ColumnWidths(Size.Auto(), Size.Fraction(1f))
            .Gap(2);

        foreach (var v in verifications)
        {
            var badge = new Badge(v.Status.ToString()).Variant(
                Constants.VerificationStatusBadgeVariants.GetValueOrDefault(v.Status, BadgeVariant.Outline));

            object name = verificationReports.GetValueOrDefault(v.Name)
                ? new Button(v.Name).Inline().OnClick(() => openVerification(v.Name))
                : Text.Block(v.Name);

            grid |= badge;
            grid |= name;
        }

        return new Card(grid).Header("Verifications").Width(Size.Px(300));
    }
}

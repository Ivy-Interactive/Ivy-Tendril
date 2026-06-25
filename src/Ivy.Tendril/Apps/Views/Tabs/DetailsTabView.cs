using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Apps.Views.Tabs;

public class DetailsTabView(
    PlanFile plan,
    List<JobItem> jobs,
    Action<string> showDebug,
    IPlanReaderService planService,
    IState<PlanFile?> selectedPlanState,
    Action refreshPlans,
    Action<string> showPlanSheet) : ViewBase
{
    public override object Build()
    {
        var navigateToPlan = Context.UsePlanNavigation(planService, showPlanSheet);
        var planYaml = PlanYamlHelper.ParsePlanYaml(plan.PlanYamlRaw);

        var detailsData = new
        {
            PlanId = plan.Id.ToString("D5"),
            Folder = plan.FolderPath,
            plan.InitialPrompt,
            Revision = plan.RevisionCount,
            Profile = Capitalize(planYaml?.ExecutionProfile),
            // Null (not an empty list) so Details.RemoveEmpty() drops these rows. Empty-collection
            // handling in RemoveEmpty needs Ivy >= the release that fixes ValidationHelper.IsEmptyContent;
            // this keeps the rows hidden with the currently referenced package.
            RelatedPlans = ParsePlanLinks(plan.RelatedPlans) is { Count: > 0 } related ? related : null,
            DependsOn = ParsePlanLinks(plan.DependsOn) is { Count: > 0 } dependsOn ? dependsOn : null,
            Issue = plan.SourceUrl ?? "",
            Created = plan.Created.ToString("yyyy-MM-dd"),
            plan.Level,
            plan.Project,
            State = plan.Status.ToString()
        };

        var details = detailsData.ToDetails()
            .Builder(x => x.PlanId, f => f.CopyToClipboard())
            .Builder(x => x.Folder, f => f.CopyToClipboard())
            .Multiline(x => x.InitialPrompt)
            .Builder(x => x.Revision, f => f.Func((int count) =>
                Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                | Text.Block(count.ToString())
                | new Button().Icon(Icons.Undo).Outline().Small()
                    .Tooltip("Revert to previous revision")
                    .Disabled(count <= 1)
                    .OnClick(() =>
                    {
                        planService.RevertRevision(plan.FolderName);
                        var updated = planService.GetPlanByFolder(plan.FolderPath);
                        if (updated != null) selectedPlanState.Set(updated);
                        refreshPlans();
                    })))
            .Builder(x => x.RelatedPlans, f => f.Func((List<Link> links) =>
                new LinkListView(links, href =>
                {
                    if (int.TryParse(href, out var planId))
                        navigateToPlan(planId);
                })))
            .Builder(x => x.DependsOn, f => f.Func((List<Link> links) =>
                new LinkListView(links, href =>
                {
                    if (int.TryParse(href, out var planId))
                        navigateToPlan(planId);
                })))
            .Builder(x => x.Issue, f => f.Link(target: LinkTarget.Blank))
        .RemoveEmpty();

        return Layout.Vertical().Gap(4)
               | details
               | (jobs.Count > 0
                   ? (Layout.Vertical().Gap(2)
                      | Text.H4("Jobs")
                      | new PlanJobsDataTableView(jobs, showDebug))
                   : null);
    }

    // Execution profiles are stored lowercase (e.g. "balanced"); present them title-cased.
    private static string Capitalize(string? value) =>
        string.IsNullOrEmpty(value) ? "" : char.ToUpperInvariant(value[0]) + value[1..];

    private static List<Link> ParsePlanLinks(List<string> planFolders)
    {
        return planFolders.Select(folder =>
        {
            var fileName = Path.GetFileName(folder);
            var dashIdx = fileName.IndexOf('-');
            var planIdStr = dashIdx > 0 ? fileName[..dashIdx] : fileName;
            planIdStr = planIdStr.TrimStart('0');
            return new Link(planIdStr, $"{planIdStr}");
        }).ToList();
    }
}

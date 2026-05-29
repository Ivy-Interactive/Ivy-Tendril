using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Views.Tabs;

public class DetailsTabView(PlanFile plan, List<JobItem> jobs, Action<string> showDebug) : ViewBase
{
    public override object Build()
    {
        var planYaml = PlanYamlHelper.ParsePlanYaml(plan.PlanYamlRaw);

        var detailsData = new
        {
            plan.InitialPrompt,
            Profile = planYaml?.ExecutionProfile ?? "",
            RelatedPlans = FormatPlanLinks(plan.RelatedPlans),
            DependsOn = FormatPlanLinks(plan.DependsOn),
            Issue = plan.SourceUrl ?? "",
            Created = plan.Created.ToString("yyyy-MM-dd"),
            plan.Level,
            plan.Project,
            State = plan.Status.ToString()
        };

        var details = detailsData.ToDetails()
            .Multiline(x => x.InitialPrompt)
            .Builder(x => x.Issue, f => f.Link())
            .RemoveEmpty();

        return Layout.Vertical().Gap(4)
               | details
               | (jobs.Count > 0
                   ? (Layout.Vertical().Gap(2)
                      | Text.H4("Jobs")
                      | new PlanJobsDataTableView(jobs, showDebug))
                   : null);
    }

    private static string FormatPlanLinks(List<string> planFolders)
    {
        if (planFolders.Count == 0)
            return "";

        var links = planFolders.Select(folder =>
        {
            var fileName = Path.GetFileName(folder);
            var dashIdx = fileName.IndexOf('-');
            var planId = dashIdx > 0 ? fileName[..dashIdx] : fileName;
            return $"[{planId}](plan://{planId})";
        });

        return string.Join(", ", links);
    }
}

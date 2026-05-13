using Ivy.Tendril.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ivy.Tendril.Views.Tabs;

public class DetailsTabView(PlanFile plan) : ViewBase
{
    public override object Build()
    {
        var planYaml = ParsePlanYaml(plan.PlanYamlRaw);

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

        return detailsData.ToDetails()
            .Multiline(x => x.InitialPrompt)
            .RemoveEmpty();
    }

    //todo claude: seems there should be a shared functions somewhere for this?
    private static PlanYaml? ParsePlanYaml(string yamlRaw)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            return deserializer.Deserialize<PlanYaml>(yamlRaw);
        }
        catch
        {
            return null;
        }
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

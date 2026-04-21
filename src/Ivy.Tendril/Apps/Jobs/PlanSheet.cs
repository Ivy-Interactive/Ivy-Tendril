using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs;

public class PlanSheet(
    string planPath,
    IPlanReaderService planService,
    IConfigService config,
    IState<string?> openFile) : ViewBase
{
    public override object Build()
    {
        var folderName = Path.GetFileName(planPath);
        var content = planService.ReadLatestRevision(folderName);
        var plan = planService.GetPlanByFolder(planPath);

        object sheetContent = string.IsNullOrEmpty(content)
            ? Text.P("Plan not found or empty.")
            : new Markdown(MarkdownHelper.AnnotateAllBrokenLinks(content, planService.PlansDirectory))
                .DangerouslyAllowLocalFiles()
                .OnLinkClick(FileLinkHelper.CreateFileLinkClickHandler(openFile));

        return sheetContent;
    }

    public object? BuildFileLinkSheet()
    {
        var plan = planService.GetPlanByFolder(planPath);
        var repoPaths = plan?.GetEffectiveRepoPaths(config) ?? [];
        return FileLinkHelper.BuildFileLinkSheet(
            openFile.Value, () => openFile.Set(null), repoPaths, config);
    }

    public string GetSheetTitle()
    {
        var folderName = Path.GetFileName(planPath);
        var plan = planService.GetPlanByFolder(planPath);
        return plan?.Title ?? folderName;
    }
}

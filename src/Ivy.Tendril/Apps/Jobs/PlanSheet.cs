using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs;

public class PlanSheet(
    string planPath,
    IPlanReaderService planService,
    IState<string?> openFile) : ViewBase
{
    public override object Build()
    {
        var folderName = Path.GetFileName(planPath);
        var content = planService.ReadLatestRevision(folderName);

        object sheetContent = string.IsNullOrEmpty(content)
            ? Text.P("Plan not found or empty.")
            : new Markdown(MarkdownHelper.AnnotateAllBrokenLinks(content, planService.PlansDirectory))
                .Article()
                .DangerouslyAllowLocalFiles()
                .OnLinkClick(FileSheet.CreateLinkClickHandler(openFile));

        return sheetContent;
    }

    public string GetSheetTitle()
    {
        var folderName = Path.GetFileName(planPath);
        var plan = planService.GetPlanByFolder(planPath);
        return plan?.Title ?? folderName;
    }
}

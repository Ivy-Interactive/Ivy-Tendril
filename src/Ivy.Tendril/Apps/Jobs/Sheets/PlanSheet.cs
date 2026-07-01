using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs.Sheets;

public class PlanSheet(
    string planPath,
    IPlanReaderService planService,
    IState<string?> openFile,
    IConfigService config) : ViewBase
{
    public override object Build()
    {
        var folderName = Path.GetFileName(planPath);
        var content = planService.ReadLatestRevision(folderName);

        object sheetContent = string.IsNullOrEmpty(content)
            ? Text.P("Plan not found or empty.")
            : new Markdown(MarkdownHelper.PrepareForDisplay(content, config))
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

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
    private readonly string _planPath = planPath;
    private readonly IPlanReaderService _planService = planService;
    private readonly IConfigService _config = config;
    private readonly IState<string?> _openFile = openFile;

    public override object Build()
    {
        var folderName = Path.GetFileName(_planPath);
        var content = _planService.ReadLatestRevision(folderName);
        var plan = _planService.GetPlanByFolder(_planPath);

        object sheetContent = string.IsNullOrEmpty(content)
            ? Text.P("Plan not found or empty.")
            : new Markdown(MarkdownHelper.AnnotateAllBrokenLinks(content, _planService.PlansDirectory))
                .DangerouslyAllowLocalFiles()
                .OnLinkClick(FileLinkHelper.CreateFileLinkClickHandler(_openFile));

        return sheetContent;
    }

    public object? BuildFileLinkSheet()
    {
        var plan = _planService.GetPlanByFolder(_planPath);
        var repoPaths = plan?.GetEffectiveRepoPaths(_config) ?? [];
        return FileLinkHelper.BuildFileLinkSheet(
            _openFile.Value, () => _openFile.Set(null), repoPaths, _config);
    }

    public string GetSheetTitle()
    {
        var folderName = Path.GetFileName(_planPath);
        var plan = _planService.GetPlanByFolder(_planPath);
        return plan?.Title ?? folderName;
    }
}

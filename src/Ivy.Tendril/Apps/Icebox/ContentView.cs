using Ivy.Core;
using Ivy.Tendril.Apps.Icebox.Dialogs;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Icebox;

public class ContentView(
    PlanFile? selectedPlan,
    List<PlanFile> allPlans,
    IState<PlanFile?> selectedPlanState,
    IPlanReaderService planService,
    IJobService jobService,
    Action refreshPlans,
    IConfigService config) : ViewBase
{
    private readonly List<PlanFile> _allPlans = allPlans;
    private readonly IConfigService _config = config;
    private readonly IJobService _jobService = jobService;
    private readonly IPlanReaderService _planService = planService;
    private readonly Action _refreshPlans = refreshPlans;
    private readonly PlanFile? _selectedPlan = selectedPlan;
    private readonly IState<PlanFile?> _selectedPlanState = selectedPlanState;

    public override object Build()
    {
        var downloadUrl = PlanDownloadHelper.UsePlanDownload(Context, _planService, _selectedPlan);
        var client = UseService<IClientProvider>();
        var copyToClipboard = UseClipboard();
        var deleteDialogOpen = UseState(false);
        var openFile = UseState<string?>(null);

        if (_selectedPlan is null)
            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a plan from the sidebar");

        var currentIndex = _allPlans.FindIndex(p => p.FolderName == _selectedPlan.FolderName);

        var header = Layout.Horizontal().Width(Size.Full()).Padding(1).Gap(2)
                     | Text.Block($"#{_selectedPlan.Id} {_selectedPlan.Title}").Bold()
                     | new Spacer().Width(Size.Grow())
                     | Text.Rich()
                         .Bold($"{currentIndex + 1}/{_allPlans.Count}", word: true)
                         .Muted("plans", word: true)
            ;

        var scrollableContent = Layout.Vertical().Width(Size.Auto().Max(Size.Units(200)))
                                |
                                new Markdown(MarkdownHelper.AnnotateAllBrokenLinks(_selectedPlan.LatestRevisionContent, _planService.PlansDirectory))
                                    .DangerouslyAllowLocalFiles()
                                    .OnLinkClick(FileLinkHelper.CreateFileLinkClickHandler(openFile, planId =>
                                    {
                                        var planFolder = Directory.GetDirectories(_planService.PlansDirectory, $"{planId:D5}-*")
                                            .FirstOrDefault();
                                        if (planFolder != null)
                                        {
                                            var plan = _planService.GetPlanByFolder(planFolder);
                                            if (plan != null)
                                                _selectedPlanState.Set(plan);
                                        }
                                    }));

        var actionBar = Layout.Horizontal().AlignContent(Align.Center).Gap(2).Padding(1)
                        | new Button("Delete").Icon(Icons.Trash).Outline().OnClick(() => deleteDialogOpen.Set(true))
                        | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(() => GoToPrevious())
                            .ShortcutKey("p")
                        | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(() => GoToNext())
                            .ShortcutKey("n")
                        | new Button("Thaw").Icon(Icons.Flame).Primary().OnClick(() =>
                        {
                            _planService.TransitionState(_selectedPlan.FolderName, PlanStatus.Draft);
                            _refreshPlans();
                        })
                        | new Button("Execute").Icon(Icons.Rocket).Outline().ShortcutKey("e").OnClick(() =>
                        {
                            _planService.TransitionState(_selectedPlan.FolderName, PlanStatus.Building);
                            _jobService.StartJob("ExecutePlan", _selectedPlan.FolderPath);
                            _refreshPlans();
                        })
                        | new Button().Icon(Icons.EllipsisVertical).Ghost().WithDropDown(
                            new MenuItem("Download", Icon: Icons.Download, Tag: "Download").OnSelect(() =>
                            {
                                var url = downloadUrl.Value;
                                if (!string.IsNullOrEmpty(url)) client.OpenUrl(url);
                            }),
                            new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
                                .OnSelect(() =>
                                {
                                    copyToClipboard(_selectedPlan.FolderPath);
                                    client.Toast("Copied path to clipboard", "Path Copied");
                                }),
                            new MenuItem("Open plan.yaml", Icon: Icons.FileText, Tag: "OpenPlanYaml").OnSelect(() =>
                            {
                                var yamlPath = Path.Combine(_selectedPlan.FolderPath, "plan.yaml");
                                _config.OpenInEditor(yamlPath);
                            })
                        );

        var mainContent = Layout.Vertical()
                          | scrollableContent;

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                mainContent
            ).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full()).Key(_selectedPlan.Id);

        var elements = new List<object>
        {
            mainLayout,
            new DeletePlanDialog(deleteDialogOpen, _selectedPlan, _planService, _refreshPlans)
        };

        var repoPaths = _selectedPlan.GetEffectiveRepoPaths(_config);
        var fileLinkSheet = FileLinkHelper.BuildFileLinkSheet(
            openFile.Value, () => openFile.Set(null), repoPaths, _config);
        if (fileLinkSheet is not null)
            elements.Add(fileLinkSheet);

        return new Fragment(elements.ToArray());
    }

    private void GoToNext()
    {
        if (_allPlans.Count == 0) return;
        var currentIndex = _allPlans.FindIndex(p => p.FolderName == _selectedPlan?.FolderName);
        var nextIndex = (currentIndex + 1) % _allPlans.Count;
        _selectedPlanState.Set(_allPlans[nextIndex]);
    }

    private void GoToPrevious()
    {
        if (_allPlans.Count == 0) return;
        var currentIndex = _allPlans.FindIndex(p => p.FolderName == _selectedPlan?.FolderName);
        var prevIndex = (currentIndex - 1 + _allPlans.Count) % _allPlans.Count;
        _selectedPlanState.Set(_allPlans[prevIndex]);
    }
}

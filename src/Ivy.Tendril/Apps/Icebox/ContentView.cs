using Ivy.Core;
using Ivy.Tendril.Apps.Icebox.Dialogs;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Views;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

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
    public override object Build()
    {
        var downloadUrl = PlanDownloadHelper.UsePlanDownload(Context, planService, selectedPlan);
        var client = UseService<IClientProvider>();
        var copyToClipboard = UseClipboard();
        var deleteDialogOpen = UseState(false);
        var openFile = UseState<string?>(null);

        if (selectedPlan is null)
        {
            if (allPlans.Count == 0)
                return new NoContentView("Icebox is empty", "Plans you put on ice will appear here.");

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a plan from the sidebar");
        }

        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan.FolderName);

        var header = Layout.Horizontal().Width(Size.Full()).Height(Size.Px(40)).Gap(2)
                     | Text.Block($"#{selectedPlan.Id} {selectedPlan.Title}").Bold()
                     | new Spacer().Width(Size.Grow())
                     | Text.Rich()
                         .Bold($"{currentIndex + 1}/{allPlans.Count}", word: true)
                         .Muted("plans", word: true)
            ;

        var scrollableContent = Layout.Vertical().Width(Size.Full().Max(Size.Units(200)))
                                |
                                new Markdown(MarkdownHelper.AnnotateAllBrokenLinks(selectedPlan.LatestRevisionContent, planService.PlansDirectory))
                                    .DangerouslyAllowLocalFiles()
                                    .OnLinkClick(FileLinkHelper.CreateFileLinkClickHandler(openFile, planId =>
                                    {
                                        var planFolder = Directory.GetDirectories(planService.PlansDirectory, $"{planId:D5}-*")
                                            .FirstOrDefault();
                                        if (planFolder != null)
                                        {
                                            var plan = planService.GetPlanByFolder(planFolder);
                                            if (plan != null)
                                                selectedPlanState.Set(plan);
                                        }
                                    }));

        var actionBar = Layout.Horizontal().AlignContent(Align.Left).Gap(1)
                        | new Button("Delete").Icon(Icons.Trash).Outline().OnClick(() => deleteDialogOpen.Set(true))
                        | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(() => GoToPrevious())
                            .ShortcutKey("p")
                        | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(() => GoToNext())
                            .ShortcutKey("n")
                        | new Button("Thaw").Icon(Icons.Flame).Primary().OnClick(() =>
                        {
                            planService.TransitionState(selectedPlan.FolderName, PlanStatus.Draft);
                            refreshPlans();
                        })
                        | new Button("Execute").Icon(Icons.Rocket).Outline().ShortcutKey("e").OnClick(() =>
                        {
                            planService.TransitionState(selectedPlan.FolderName, PlanStatus.Building);
                            jobService.StartJob(Constants.JobTypes.ExecutePlan, selectedPlan.FolderPath);
                            refreshPlans();
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
                                    copyToClipboard(selectedPlan.FolderPath);
                                    client.Toast("Copied path to clipboard", "Path Copied");
                                }),
                            new MenuItem("Open plan.yaml", Icon: Icons.FileText, Tag: "OpenPlanYaml").OnSelect(() =>
                            {
                                var yamlPath = Path.Combine(selectedPlan.FolderPath, "plan.yaml");
                                config.OpenInEditor(yamlPath);
                            })
                        );

        var mainContent = Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full())
                          | scrollableContent;

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                mainContent
            ).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full()).Key(selectedPlan.Id);

        var elements = new List<object>
        {
            mainLayout,
            new DeletePlanDialog(deleteDialogOpen, selectedPlan, planService, refreshPlans)
        };

        var repoPaths = selectedPlan.GetEffectiveRepoPaths(config);
        var fileLinkSheet = FileLinkHelper.BuildFileLinkSheet(
            openFile.Value, () => openFile.Set(null), repoPaths, config);
        if (fileLinkSheet is not null)
            elements.Add(fileLinkSheet);

        return new Fragment(elements.ToArray());
    }

    private void GoToNext()
    {
        if (allPlans.Count == 0) return;
        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan?.FolderName);
        var nextIndex = (currentIndex + 1) % allPlans.Count;
        selectedPlanState.Set(allPlans[nextIndex]);
    }

    private void GoToPrevious()
    {
        if (allPlans.Count == 0) return;
        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan?.FolderName);
        var prevIndex = (currentIndex - 1 + allPlans.Count) % allPlans.Count;
        selectedPlanState.Set(allPlans[prevIndex]);
    }
}

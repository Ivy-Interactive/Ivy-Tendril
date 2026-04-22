using Ivy.Core;
using Ivy.Tendril.Apps.Icebox.Dialogs;
using Ivy.Tendril.Apps.Plans;
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

        var isEditing = UseState(false);
        var editContent = UseState("");
        var originalContent = UseState("");
        var isEditingPrev = UseState(false);
        var lastPlanId = UseState(selectedPlan?.Id ?? -1);

        var selectedPlanRef = UseRef(selectedPlan);

        UseEffect(() =>
        {
            var plan = selectedPlanRef.Value;
            if (isEditing.Value && !isEditingPrev.Value)
            {
                if (plan != null)
                {
                    var raw = planService.ReadRawPlan(plan.FolderName);
                    editContent.Set(raw);
                    originalContent.Set(raw);
                }
                else
                {
                    isEditing.Set(false);
                }
            }
            else if (!isEditing.Value && isEditingPrev.Value)
            {
                if (plan != null && editContent.Value != originalContent.Value)
                {
                    planService.SaveRevision(plan.FolderName, editContent.Value);
                    refreshPlans();
                }
            }

            isEditingPrev.Set(isEditing.Value);
        }, isEditing);

#pragma warning disable CS8601
        selectedPlanRef.Value = selectedPlan;
#pragma warning restore CS8601

        if (lastPlanId.Value != (selectedPlan?.Id ?? -1))
        {
            lastPlanId.Set(selectedPlan?.Id ?? -1);
            isEditing.Set(false);
        }

        if (selectedPlan is null)
            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a plan from the sidebar");

        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan.FolderName);

        var header = Layout.Horizontal().Width(Size.Full()).Height(Size.Px(40)).Gap(2)
                     | Text.Block($"#{selectedPlan.Id} {selectedPlan.Title}").Bold()
                     | isEditing.ToSwitchInput(Icons.Pencil).Label("Edit")
                     | new Spacer().Width(Size.Grow())
                     | Text.Rich()
                         .Bold($"{currentIndex + 1}/{allPlans.Count}", word: true)
                         .Muted("plans", word: true)
            ;

        var scrollableContent = Layout.Vertical().Width(Size.Auto().Max(Size.Units(200)));

        if (isEditing.Value)
            scrollableContent |= editContent.ToCodeInput()
                .Language(Languages.Markdown)
                .Width(Size.Full())
                .OnBlur(() =>
                {
                    var plan = selectedPlanRef.Value;
                    if (plan != null && editContent.Value != originalContent.Value)
                    {
                        planService.SaveRevision(plan.FolderName, editContent.Value);
                        originalContent.Set(editContent.Value);
                        refreshPlans();
                    }
                });
        else
            scrollableContent |=
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
                            jobService.StartJob("ExecutePlan", selectedPlan.FolderPath);
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

        var mainContent = Layout.Vertical()
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

using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Apps.Recommendations.Dialogs;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Recommendations;

public class ContentView(
    Recommendation? selectedRecommendation,
    List<Recommendation> allRecommendations,
    IState<Recommendation?> selectedState,
    IPlanReaderService planService,
    IJobService jobService,
    Action refresh) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var config = UseService<IConfigService>();
        var copyToClipboard = UseClipboard();
        var showPlan = UseState<string?>(null);
        var openFile = UseState<string?>(null);
        var showNotesDialog = UseState(false);
        var showDeclineDialog = UseState<bool>();

        if (selectedRecommendation is null)
        {
            if (allRecommendations.Count == 0)
                return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()).Gap(2)
                       | new Icon(Icons.Inbox).Large().Color(Colors.Gray)
                       | Text.Muted("No recommendations yet");

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a recommendation from the sidebar");
        }

        var currentIndex = allRecommendations.FindIndex(r => r.PlanId == selectedRecommendation.PlanId && r.Title == selectedRecommendation.Title);

        // Header with Accept action at right edge
        var header = Layout.Horizontal().Width(Size.Full()).Height(Size.Px(40)).Gap(2)
                     | Text.Block($"#{selectedRecommendation.PlanId} {selectedRecommendation.Title}").Bold()
                     | new Badge(selectedRecommendation.Project).Variant(BadgeVariant.Outline)
                         .WithProjectColor(config, selectedRecommendation.Project)
                     | new Spacer().Width(Size.Grow())
                     | Text.Rich()
                         .Bold($"{currentIndex + 1}/{allRecommendations.Count}", word: true)
                         .Muted("recommendations", word: true)
                     | new Button("Decline").Icon(Icons.X).Outline().ShortcutKey("x").OnClick(() =>
                     {
                         showDeclineDialog.Set(true);
                     })
                     | new Button("Accept").Icon(Icons.Check).Primary().ShortcutKey("a").OnClick(() =>
                     {
                         planService.UpdateRecommendationState(selectedRecommendation.PlanFolderName, selectedRecommendation.Title, "Accepted");
                         jobService.StartJob("CreatePlan", "-Description", selectedRecommendation.Description, "-Project",
                             selectedRecommendation.Project);
                         client.Toast($"Started CreatePlan: {selectedRecommendation.Title}", "Recommendation Accepted");
                         refresh();
                         GoToNext();
                     });

        // Content
        var scrollableContent = Layout.Vertical().Width(Size.Full().Max(Size.Units(200))).Gap(4).Padding(2);

        // Source plan info and Impact/Risk badges
        var metaRow = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                      | Text.Muted($"Plan #{selectedRecommendation.PlanId}: {selectedRecommendation.PlanTitle}");

        if (selectedRecommendation.Impact is { } impact)
            metaRow |= new Badge($"Impact: {impact}").Variant(impact switch
            {
                "High" => BadgeVariant.Success,
                "Medium" => BadgeVariant.Warning,
                _ => BadgeVariant.Outline
            });

        if (selectedRecommendation.Risk is { } risk)
            metaRow |= new Badge($"Risk: {risk}").Variant(risk switch
            {
                "High" => BadgeVariant.Destructive,
                "Medium" => BadgeVariant.Warning,
                _ => BadgeVariant.Success
            });

        scrollableContent |= Layout.Vertical().Gap(1)
                             | Text.Block("Source Plan").Bold()
                             | metaRow;

        // Description
        scrollableContent |= new Separator();
        scrollableContent |= new Markdown(selectedRecommendation.Description);

        // Action bar (secondary actions)
        var actionBar = Layout.Horizontal().AlignContent(Align.Left).Gap(1)
                        | new Button("Accept with Notes").Icon(Icons.CircleCheck).Outline().ShortcutKey("w")
                            .OnClick(() => showNotesDialog.Set(true))
                        | new Button("View Plan").Icon(Icons.ExternalLink).Outline().ShortcutKey("d").OnClick(() =>
                        {
                            var fullPath = Path.Combine(planService.PlansDirectory, selectedRecommendation.PlanFolderName);
                            if (Directory.Exists(fullPath))
                                showPlan.Set(fullPath);
                        })
                        | new Button("Previous").Icon(Icons.ChevronLeft).Outline().ShortcutKey("p")
                            .OnClick(() => GoToPrevious())
                        | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().ShortcutKey("n")
                            .OnClick(() => GoToNext())
                        | new Button().Icon(Icons.EllipsisVertical).Ghost().WithDropDown(
                            new MenuItem("Open in File Manager", Icon: Icons.FolderOpen, Tag: "OpenInExplorer")
                                .OnSelect(() =>
                                {
                                    var fullPath = Path.Combine(planService.PlansDirectory, selectedRecommendation.PlanFolderName);
                                    if (Directory.Exists(fullPath))
                                        PlatformHelper.OpenInFileManager(fullPath);
                                }),
                            new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
                                .OnSelect(() =>
                                {
                                    var fullPath = Path.Combine(planService.PlansDirectory, selectedRecommendation.PlanFolderName);
                                    copyToClipboard(fullPath);
                                    client.Toast("Copied path to clipboard", "Path Copied");
                                }),
                            new MenuItem("Open plan.yaml", Icon: Icons.FileText, Tag: "OpenPlanYaml").OnSelect(() =>
                            {
                                var fullPath = Path.Combine(planService.PlansDirectory, selectedRecommendation.PlanFolderName);
                                var yamlPath = Path.Combine(fullPath, "plan.yaml");
                                config.OpenInEditor(yamlPath);
                            })
                        );

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                scrollableContent
            ).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full());

        var notesDialog = new AcceptWithNotesDialog(
            showNotesDialog,
            selectedRecommendation,
            notes =>
            {
                var description = $"[ORIGINAL RECOMMENDATION]\n{selectedRecommendation.Description}\n\n[NOTES]\n{notes}";
                planService.UpdateRecommendationState(selectedRecommendation.PlanFolderName, selectedRecommendation.Title, "AcceptedWithNotes");
                jobService.StartJob("CreatePlan", "-Description", description, "-Project", selectedRecommendation.Project);
                client.Toast($"Started CreatePlan: {selectedRecommendation.Title}", "Recommendation Accepted with Notes");
                refresh();
                GoToNext();
            });

        var declineDialog = new DeclineRecommendationDialog(
            showDeclineDialog, selectedRecommendation, planService, refresh, GoToNext);

        if (showPlan.Value is { } planPath)
        {
            var folderName = Path.GetFileName(planPath);
            var content = planService.ReadLatestRevision(folderName);
            var plan = planService.GetPlanByFolder(planPath);

            var sheetContent = string.IsNullOrEmpty(content)
                ? Text.P("Plan not found or empty.")
                : (object)new Markdown(MarkdownHelper.AnnotateAllBrokenLinks(content, planService.PlansDirectory))
                    .DangerouslyAllowLocalFiles()
                    .OnLinkClick(FileLinkHelper.CreateFileLinkClickHandler(openFile));

            var planSheet = new Sheet(
                () => showPlan.Set(null),
                sheetContent,
                plan?.Title ?? folderName
            ).Width(Size.Half()).Resizable();

            var repoPaths = plan?.GetEffectiveRepoPaths(config) ?? [];
            var fileLinkSheet = FileLinkHelper.BuildFileLinkSheet(
                openFile.Value,
                () => openFile.Set(null),
                repoPaths,
                config);

            if (fileLinkSheet is not null)
                return new Fragment(
                    mainLayout,
                    planSheet,
                    fileLinkSheet,
                    notesDialog,
                    declineDialog
                );

            return new Fragment(mainLayout, planSheet, notesDialog, declineDialog);
        }

        return new Fragment(mainLayout, notesDialog, declineDialog);
    }

    private void GoToNext()
    {
        if (allRecommendations.Count == 0) return;
        var currentIndex = allRecommendations.FindIndex(r => r.PlanId == selectedRecommendation?.PlanId && r.Title == selectedRecommendation?.Title);
        var nextIndex = (currentIndex + 1) % allRecommendations.Count;
        selectedState.Set(allRecommendations[nextIndex]);
    }

    private void GoToPrevious()
    {
        if (allRecommendations.Count == 0) return;
        var currentIndex = allRecommendations.FindIndex(r => r.PlanId == selectedRecommendation?.PlanId && r.Title == selectedRecommendation?.Title);
        var prevIndex = (currentIndex - 1 + allRecommendations.Count) % allRecommendations.Count;
        selectedState.Set(allRecommendations[prevIndex]);
    }
}

using Ivy.Tendril.Apps.Recommendations.Dialogs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Models;
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
        var openFile = UseState<string?>(null);
        var (planSheet, showPlan) = UseTrigger<string>((isOpen, planPath) =>
        {
            if (!isOpen.Value) return null;
            var folderName = Path.GetFileName(planPath);
            var content = planService.ReadLatestRevision(folderName);
            var plan = planService.GetPlanByFolder(planPath);

            var sheetContent = string.IsNullOrEmpty(content)
                ? Text.P("Plan not found or empty.")
                : (object)new Markdown(MarkdownHelper.AnnotateAllBrokenLinks(content, planService.PlansDirectory))
                    .DangerouslyAllowLocalFiles()
                    .Article()
                    .OnLinkClick(FileSheet.CreateLinkClickHandler(openFile));

            var sheet = new Sheet(
                () => isOpen.Set(false),
                sheetContent,
                plan?.Title ?? folderName
            ).Width(UxHelper.SheetWidth).Resizable();

            return new Fragment(sheet, new FileSheet(openFile, config));
        });
        var (notesDialog, showNotesDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value || selectedRecommendation is null) return null;
            return new AcceptWithNotesDialog(
                isOpen,
                selectedRecommendation,
                notes =>
                {
                    var description = $"[ORIGINAL RECOMMENDATION]\n{selectedRecommendation.Description}\n\n[NOTES]\n{notes}";
                    planService.UpdateRecommendationState(selectedRecommendation.PlanFolderName, selectedRecommendation.Title, RecommendationStatus.AcceptedWithNotes);
                    jobService.StartJob(new CreatePlanArgs(description, selectedRecommendation.Project));
                    client.Toast($"Started CreatePlan: {selectedRecommendation.Title}", "Recommendation Accepted with Notes");
                    refresh();
                    GoToNext();
                });
        });


        if (selectedRecommendation is null)
        {
            if (allRecommendations.Count == 0)
                return new NoContentView("No recommendations", "Recommendations from completed plans will appear here");

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a recommendation from the sidebar");
        }
        var currentIndex = allRecommendations.FindIndex(r => r.PlanId == selectedRecommendation.PlanId && r.Title == selectedRecommendation.Title);

        object BuildTitleArea()
        {
            var desktopTitleLayout = Layout.Horizontal().Gap(2).AlignContent(Align.Left).Width(Size.Full())
                | new Box(Text.Block($"#{selectedRecommendation.PlanId} {selectedRecommendation.Title}").Bold().NoWrap().Overflow(Overflow.Ellipsis))
                    .BorderThickness(0).Padding(0).Width(Size.Fit())
                | new Badge(selectedRecommendation.Project).Variant(BadgeVariant.Outline)
                    .WithProjectColor(config, selectedRecommendation.Project);

            var desktopTitle = new Box(desktopTitleLayout).BorderThickness(0).Padding(0)
                .HideOn(Breakpoint.Mobile, Breakpoint.Tablet);

            return Layout.Vertical().Gap(1).AlignContent(Align.Left).Width(Size.Grow())
                   | desktopTitle
                   | MobileItemPicker.Build(
                           $"#{selectedRecommendation.PlanId} {selectedRecommendation.Title}",
                           allRecommendations,
                           r => $"#{r.PlanId} {r.Title}",
                           r => r.PlanId == selectedRecommendation.PlanId && r.Title == selectedRecommendation.Title,
                           r => selectedState.Set(r))
                       .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);
        }

        object BuildControls() => Layout.Horizontal().Gap(2).AlignContent(Align.Right)
                       | Text.Rich()
                           .Bold($"{(currentIndex == -1 ? "?" : (currentIndex + 1).ToString())}/{allRecommendations.Count}", word: true)
                           .Muted("recommendations", word: true)
                       | new Button("Decline").Icon(Icons.X).Outline().ShortcutKey("Backspace").OnClick(() =>
                       {
                           planService.UpdateRecommendationState(selectedRecommendation.PlanFolderName, selectedRecommendation.Title, RecommendationStatus.Declined);
                           refresh();
                           GoToNext();
                       })
                       | new Button("Accept").Icon(Icons.Check).Primary().ShortcutKey("a").OnClick(() =>
                       {
                           planService.UpdateRecommendationState(selectedRecommendation.PlanFolderName, selectedRecommendation.Title, RecommendationStatus.Accepted);
                           jobService.StartJob(new CreatePlanArgs(selectedRecommendation.Description, selectedRecommendation.Project));
                           client.Toast($"Started CreatePlan: {selectedRecommendation.Title}", "Recommendation Accepted");
                           refresh();
                           GoToNext();
                       });

        var header = ResponsiveHeader.Build(BuildTitleArea, BuildControls);

        // Content
        var scrollableContent = Layout.Vertical().Width(Size.Full().Max(Size.Units(200))).Padding(6, 2, 6, 2);

        // Source plan info and Impact badge
        var metaRow = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                      | Text.Muted($"Plan #{selectedRecommendation.PlanId}: {selectedRecommendation.PlanTitle}");

        if (selectedRecommendation.Impact is { } impact)
            metaRow |= new Badge($"Impact: {impact}").Variant(impact switch
            {
                "High" => BadgeVariant.Success,
                "Medium" => BadgeVariant.Warning,
                _ => BadgeVariant.Outline
            });

        scrollableContent |= Layout.Vertical().Gap(1)
                             | Text.Block("Source Plan").Bold()
                             | metaRow;

        // Description
        scrollableContent |= new Separator();
        scrollableContent |= new Markdown(selectedRecommendation.Description);

        // Standard overflow menu items
        var standardOverflowItems = new[]
        {
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
                try
                {
                    config.OpenInEditor(yamlPath);
                }
                catch (EditorNotAvailableException ex)
                {
                    client.Toast(
                        $"'{ex.Command}' not found in PATH. Install the shell command from {ex.Label} or update the editor command in Settings → Advanced.",
                        "Editor Not Available",
                        variant: ToastVariant.Destructive);
                }
            })
        };

        void ViewPlan()
        {
            var fullPath = Path.Combine(planService.PlansDirectory, selectedRecommendation.PlanFolderName);
            if (Directory.Exists(fullPath))
                showPlan(fullPath);
        }

        // Full-tier dropdown: standard overflow items only (all buttons shown inline)
        var fullDropdownItems = standardOverflowItems;

        // Compact-tier dropdown: View Plan + standard overflow
        var compactDropdownItems = new List<MenuItem>
        {
            new MenuItem("View Plan", Icon: Icons.ExternalLink, Tag: "ViewPlan").OnSelect(ViewPlan)
        };
        compactDropdownItems.AddRange(standardOverflowItems);

        // Minimal-tier dropdown: Accept with Notes, View Plan + standard overflow
        var minimalDropdownItems = new List<MenuItem>
        {
            new MenuItem("Accept with Notes", Icon: Icons.CircleCheck, Tag: "AcceptWithNotes")
                .OnSelect(() => showNotesDialog()),
            new MenuItem("View Plan", Icon: Icons.ExternalLink, Tag: "ViewPlan").OnSelect(ViewPlan)
        };
        minimalDropdownItems.AddRange(standardOverflowItems);

        // Action bar without .Wrap() - single row with progressive collapse.
        // Full (>=1024px): Previous, Next, Accept with Notes, View Plan inline + overflow dropdown.
        // Compact (768-1023px): Previous, Next, Accept with Notes inline; View Plan in dropdown.
        // Minimal (<768px): Previous, Next inline; everything else in dropdown.
        var actionBar = Layout.Horizontal().AlignContent(Align.Left).Gap(2)
                        | new Button("Previous").Icon(Icons.ChevronLeft).Outline().ShortcutKey("p")
                            .OnClick(GoToPrevious).AlwaysVisible()
                        | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().ShortcutKey("n")
                            .OnClick(GoToNext).AlwaysVisible()
                        | new Button("Accept with Notes").Icon(Icons.CircleCheck).Outline().ShortcutKey("w")
                            .OnClick(() => showNotesDialog()).CompactUp()
                        | new Button("View Plan").Icon(Icons.ExternalLink).Outline()
                            .OnClick(ViewPlan).FullOnly()
                        | ActionBarResponsive.DropdownAtFull(
                            new Button().Icon(Icons.EllipsisVertical).Ghost(),
                            fullDropdownItems)
                        | ActionBarResponsive.DropdownAtCompact(
                            new Button().Icon(Icons.EllipsisVertical).Ghost(),
                            compactDropdownItems.ToArray())
                        | ActionBarResponsive.DropdownAtMinimal(
                            new Button().Icon(Icons.EllipsisVertical).Ghost(),
                            minimalDropdownItems.ToArray());

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                scrollableContent
            ).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full());

        return new Fragment(mainLayout, planSheet, notesDialog);
    }

    private void GoToNext()
    {
        if (allRecommendations.Count == 0) return;
        var currentIndex = allRecommendations.FindIndex(r => r.PlanId == selectedRecommendation?.PlanId && r.Title == selectedRecommendation?.Title);
        if (currentIndex == -1) return; // Prevent navigation if not found
        var nextIndex = (currentIndex + 1) % allRecommendations.Count;
        selectedState.Set(allRecommendations[nextIndex]);
    }

    private void GoToPrevious()
    {
        if (allRecommendations.Count == 0) return;
        var currentIndex = allRecommendations.FindIndex(r => r.PlanId == selectedRecommendation?.PlanId && r.Title == selectedRecommendation?.Title);
        if (currentIndex == -1) return; // Prevent navigation if not found
        var prevIndex = (currentIndex - 1 + allRecommendations.Count) % allRecommendations.Count;
        selectedState.Set(allRecommendations[prevIndex]);
    }
}

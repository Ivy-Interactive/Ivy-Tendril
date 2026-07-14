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
                : (object)new Markdown(MarkdownHelper.PrepareForDisplay(content, config))
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
                },
                config);
        });


        if (selectedRecommendation is null)
        {
            if (allRecommendations.Count == 0)
                return new NoContentView("No recommendations", "Recommendations from completed plans will appear here");

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a recommendation from the sidebar");
        }
        var currentIndex = allRecommendations.FindIndex(r => r.PlanId == selectedRecommendation.PlanId && r.Title == selectedRecommendation.Title);

        object BuildTitleArea(bool isMobile)
        {
            // Project/Impact badges live on each sidebar row (see SidebarView), so the header
            // title stays badge-free.
            var desktopTitleLayout = Layout.Vertical().Gap(1).AlignContent(Align.Left).Width(Size.Full().Min(Size.Px(0)))
                | Text.Block($"#{selectedRecommendation.ShortPlanId} {selectedRecommendation.Title}").Bold().NoWrap().Overflow(Overflow.Ellipsis)
                    .Width(Size.Grow().Min(Size.Px(0)));

            var desktopTitle = new Box(desktopTitleLayout).BorderThickness(0).Padding(0)
                .Width(Size.Full().Min(Size.Px(0)))
                .HideOn(Breakpoint.Mobile, Breakpoint.Tablet);

            return Layout.Vertical().Gap(1).AlignContent(Align.Left).Width(Size.Grow().Min(Size.Px(0)))
                   | desktopTitle
                   | MobileItemPicker.Build(
                           $"#{selectedRecommendation.ShortPlanId} {selectedRecommendation.Title}",
                           allRecommendations,
                           r => $"#{r.ShortPlanId} {r.Title}",
                           r => r.PlanId == selectedRecommendation.PlanId && r.Title == selectedRecommendation.Title,
                           r => selectedState.Set(r))
                       .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);
        }

        object BuildControls(bool isMobile) => Layout.Horizontal().Gap(2).AlignContent(Align.Right)
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

        // Source plan info
        var metaRow = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                      | Text.Muted($"Plan #{selectedRecommendation.ShortPlanId}: {selectedRecommendation.PlanTitle}");

        scrollableContent |= Layout.Vertical().Gap(1)
                             | Text.Block("Source Plan").Bold()
                             | metaRow;

        // Description
        scrollableContent |= new Separator();
        scrollableContent |= new Markdown(MarkdownHelper.PrepareForDisplay(selectedRecommendation.Description, config));

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

        // Pane-Compact-tier dropdown: View Plan + standard overflow. See the pane-width note
        // on ActionBarResponsive: the Recommendations footer's content pane is narrower than
        // the viewport by the app sidebar + recommendations-list pane to its left, so even at
        // the Wide viewport tier only Previous/Next/Accept with Notes fit inline.
        var paneCompactDropdownItems = new List<MenuItem>
        {
            new MenuItem("View Plan", Icon: Icons.ExternalLink, Tag: "ViewPlan").OnSelect(ViewPlan)
        };
        paneCompactDropdownItems.AddRange(standardOverflowItems);

        // Pane-Minimal-tier dropdown: all action buttons + standard overflow
        var paneMinimalDropdownItems = new List<MenuItem>
        {
            new MenuItem("Accept with Notes", Icon: Icons.CircleCheck, Tag: "AcceptWithNotes")
                .OnSelect(() => showNotesDialog()),
            new MenuItem("View Plan", Icon: Icons.ExternalLink, Tag: "ViewPlan").OnSelect(ViewPlan)
        };
        paneMinimalDropdownItems.AddRange(standardOverflowItems);

        // Action bar without .Wrap() - the footer slot is fixed-height, so wrapping could
        // push content out; a single row with progressive collapse is required instead.
        //
        // These tiers are keyed to the CONTENT PANE, not the raw viewport — see the
        // pane-width note on ActionBarResponsive. The pane is narrower than the viewport
        // by the app sidebar + recommendations-list pane to its left, so the budget for
        // each tier is shifted down by one viewport step from what a full-width bar would use:
        // Pane-Compact tier (viewport >=1024px / Wide): Previous, Next, Accept with Notes inline; View Plan in dropdown.
        // Pane-Minimal tier (viewport <1024px): Previous, Next inline; everything else in dropdown.
        // View Plan never gets an inline slot at any viewport width — the pane is never wide
        // enough to guarantee it fits, so it's always dropdown-only.
        //
        // The framework only registers a Button's ShortcutKey while that Button is mounted
        // (useShortcut's cleanup runs on unmount), and ShowOn/HideOn/PaneCompactUp/etc. truly
        // unmount the widget rather than just hiding it with CSS (see MemoizedWidget in
        // widgetRenderer.tsx: `if (visible === false) return null`). Accept with Notes carries
        // a ShortcutKey and is dropdown-only below the Pane-Compact tier, so its shortcut has
        // to live on an icon-only Button that stays mounted at every tier instead —
        // MenuItem.Shortcut is display-only (just a Kbd hint) and never registers a keyboard
        // handler.
        var actionBar = Layout.Horizontal().AlignContent(Align.Left).Gap(2)
                        | new Button("Previous").Icon(Icons.ChevronLeft).Outline().ShortcutKey("p")
                            .OnClick(GoToPrevious).AlwaysVisible()
                        | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().ShortcutKey("n")
                            .OnClick(GoToNext).AlwaysVisible()
                        | new Button("Accept with Notes").Icon(Icons.CircleCheck).Outline()
                            .OnClick(() => showNotesDialog()).PaneCompactUp()
                        // Icon-only, always-mounted carrier for the "w" shortcut — Accept with
                        // Notes itself is dropdown-only below the Pane-Compact tier (see note
                        // above), so this keeps the keyboard shortcut alive without
                        // reintroducing a labeled inline button that can clip.
                        | new Button().Icon(Icons.CircleCheck).Ghost().ShortcutKey("w")
                            .Tooltip("Accept with Notes").OnClick(() => showNotesDialog()).AlwaysVisible()
                        // Pane-Compact-tier dropdown: View Plan + standard overflow
                        | ActionBarResponsive.DropdownAtPaneCompact(
                            new Button().Icon(Icons.EllipsisVertical).Ghost(),
                            paneCompactDropdownItems.ToArray())
                        // Pane-Minimal-tier dropdown: all action buttons + standard overflow
                        | ActionBarResponsive.DropdownAtPaneMinimal(
                            new Button().Icon(Icons.EllipsisVertical).Ghost(),
                            paneMinimalDropdownItems.ToArray());

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

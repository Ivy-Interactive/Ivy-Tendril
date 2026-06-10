using Ivy.Tendril.Apps.Trash;
using Ivy.Tendril.Apps.Trash.Dialogs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps;

public record TrashFileInfo(
    string FilePath,
    string FileName,
    DateTime Date,
    string OriginalRequest,
    string DuplicateOf,
    string Project,
    string Content);

[App(title: "Trash", icon: Icons.Trash2, group: ["Apps"], order: Constants.Trash, isVisible: false)]
public class TrashApp : ViewBase
{
    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var jobService = UseService<IJobService>();
        var planService = UseService<IPlanReaderService>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();
        var selectedFile = UseState<string?>(null);
        var searchFilter = UseState<string?>("");
        var openFile = UseState<string?>(null);

        var (deleteDialog, showDeleteDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            var selected = LoadTrashFiles(Path.Combine(configService.TendrilHome, "Trash"))
                .FirstOrDefault(f => f.FilePath == selectedFile.Value);
            return new DeleteTrashFileDialog(isOpen, selected, selectedFile, refreshToken);
        });

        UseInterval(() => refreshToken.Refresh(), TimeSpan.FromSeconds(10));

        var trashDir = Path.Combine(configService.TendrilHome, "Trash");
        var files = LoadTrashFiles(trashDir);

        // Apply search filter for selection logic
        var filteredFiles = files.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(searchFilter.Value))
        {
            var searchTerm = searchFilter.Value.ToLowerInvariant();
            filteredFiles = filteredFiles.Where(f =>
                f.FileName.ToLowerInvariant().Contains(searchTerm) ||
                f.OriginalRequest.ToLowerInvariant().Contains(searchTerm) ||
                f.Project.ToLowerInvariant().Contains(searchTerm) ||
                f.DuplicateOf.ToLowerInvariant().Contains(searchTerm)
            );
        }

        var filteredList = filteredFiles.ToList();

        // Auto-select first file if selection is invalid
        if (selectedFile.Value is { } sel && filteredList.All(f => f.FilePath != sel))
            selectedFile.Set(filteredList.FirstOrDefault()?.FilePath);

        var selected = filteredList.FirstOrDefault(f => f.FilePath == selectedFile.Value);

        var sidebar = new SidebarView(files, selectedFile, searchFilter);

        // Main content
        object mainContent;
        if (files.Count == 0)
        {
            mainContent = new NoContentView("No trash", "Duplicate plans will appear here");
        }
        else if (selected is null)
        {
            mainContent = Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                          | Text.Muted("Select a file from the sidebar");
        }
        else
        {
            var header = Layout.Horizontal().Width(Size.Full()).Wrap().Gap(2).AlignContent(Align.Left)
                         | new Box(Text.Block(selected.FileName).Bold())
                             .BorderThickness(0).Padding(0)
                             .HideOn(Breakpoint.Mobile, Breakpoint.Tablet)
                         | MobileItemPicker.Build(
                                 selected.FileName,
                                 filteredList,
                                 f => f.FileName,
                                 f => f.FilePath == selected.FilePath,
                                 f => selectedFile.Set(f.FilePath))
                             .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet)
                         | new Badge(selected.Project).Variant(BadgeVariant.Outline)
                         | (string.IsNullOrEmpty(selected.DuplicateOf)
                             ? new Fragment()
                             : Text.Muted($"Duplicate of: {selected.DuplicateOf}"));

            var actionBar = Layout.Horizontal().AlignContent(Align.Center).Gap(2).Padding(1)
                            | new Button("Delete").Icon(Icons.Trash).Outline().OnClick(() => showDeleteDialog())
                            | new Button("Force Plan").Icon(Icons.Zap).Primary().OnClick(() =>
                            {
                                if (!string.IsNullOrEmpty(selected.OriginalRequest))
                                {
                                    var project = string.IsNullOrEmpty(selected.Project) ? "Auto" : selected.Project;
                                    jobService.StartJob(new CreatePlanArgs(selected.OriginalRequest, project, Force: true));
                                    client.Toast("CreatePlan job started", "Force Plan");
                                }
                            });

            var annotatedContent = MarkdownHelper.AnnotateAllBrokenLinks(selected.Content, planService.PlansDirectory);
            var scrollableContent = Layout.Vertical().Width(Size.Full().Max(Size.Units(200))).Padding(6, 2, 6, 2)
                                    | new Markdown(annotatedContent)
                                        .DangerouslyAllowLocalFiles()
                                        .Article()
                                        .OnLinkClick(FileSheet.CreateLinkClickHandler(openFile));

            var scrollWrapper = Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full())
                                | scrollableContent;
            mainContent = new HeaderLayout(
                header,
                new FooterLayout(
                    actionBar,
                    scrollWrapper
                ).Size(Size.Full())
            ).Scroll(Scroll.None).Size(Size.Full());
        }

        var elements = new List<object>
        {
            new SidebarLayout(
                mainContent,
                sidebar
            ).SidebarContentScroll(Scroll.None).CollapsibleOnMobile()
        };

        elements.Add(new FileSheet(openFile, configService));

        elements.Add(deleteDialog);

        return new Fragment(elements.ToArray());
    }

    private static List<TrashFileInfo> LoadTrashFiles(string trashDir)
    {
        if (!Directory.Exists(trashDir))
            return [];

        return Directory.GetFiles(trashDir, "*.md")
            .Select(ParseTrashFile)
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderByDescending(f => f.Date)
            .ToList();
    }

    private static TrashFileInfo? ParseTrashFile(string filePath)
    {
        try
        {
            var content = FileHelper.ReadAllText(filePath);
            var fileName = Path.GetFileName(filePath);

            var date = DateTime.MinValue;
            var originalRequest = "";
            var duplicateOf = "";
            var project = "";
            var body = content;

            // Parse YAML frontmatter
            if (content.StartsWith("---"))
            {
                var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
                if (endIndex > 0)
                {
                    var frontmatter = content.Substring(3, endIndex - 3);
                    body = content.Substring(endIndex + 3).TrimStart('\r', '\n');

                    foreach (var line in frontmatter.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("date:"))
                        {
                            var val = trimmed.Substring("date:".Length).Trim();
                            DateTime.TryParse(val, out date);
                        }
                        else if (trimmed.StartsWith("originalRequest:"))
                        {
                            originalRequest = trimmed.Substring("originalRequest:".Length).Trim().Trim('"');
                        }
                        else if (trimmed.StartsWith("duplicateOf:"))
                        {
                            duplicateOf = trimmed.Substring("duplicateOf:".Length).Trim().Trim('"');
                        }
                        else if (trimmed.StartsWith("project:"))
                        {
                            project = trimmed.Substring("project:".Length).Trim().Trim('"');
                        }
                    }
                }
            }

            return new TrashFileInfo(filePath, fileName, date, originalRequest, duplicateOf, project, body);
        }
        catch
        {
            return null;
        }
    }
}

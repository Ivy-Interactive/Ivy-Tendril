using System.Text.RegularExpressions;
using Ivy.Core.Hooks;
using Ivy.Desktop;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Views;

public class ProjectRepoPickerView(
    IState<List<RepoRef>> repos,
    IState<string>? projectName = null,
    Func<RepoRef, Task<RepoRef?>>? onAdd = null,
    bool showPrRule = false) : ViewBase
{
    public override object Build()
    {
        var inputValue = UseState("");
        var addingError = UseState<string?>(null);
        var isAdding = UseState(false);

        Context.TryUseService<DesktopWindow>(out var desktop);
        var isDesktop = desktop != null;

        IConfigService? configService = null;
        Context.TryUseService<IConfigService>(out configService);
        var tendrilHome = configService?.TendrilHome;

        async Task AddAsync()
        {
            var path = (inputValue.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path)) return;

            if (!RepoPathValidator.IsValid(path))
            {
                addingError.Set("Invalid input. Enter an SSH URL, HTTPS URL, or local path.");
                return;
            }

            if (repos.Value.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                inputValue.Set("");
                return;
            }

            var draft = new RepoRef { Path = path, PrRule = "default" };
            var suggestedName = RepoPathValidator.ExtractRepoName(path) ?? path;

            addingError.Set(null);
            isAdding.Set(true);
            try
            {
                RepoRef? toAdd = draft;
                if (onAdd != null)
                {
                    toAdd = await onAdd(draft);
                    if (toAdd == null) return;
                }

                var list = new List<RepoRef>(repos.Value) { toAdd };
                repos.Set(list);

                if (projectName != null && string.IsNullOrEmpty(projectName.Value) && !string.IsNullOrEmpty(suggestedName))
                    projectName.Set(SanitizeProjectName(suggestedName));

                inputValue.Set("");
            }
            catch (Exception ex)
            {
                addingError.Set(ex.Message);
            }
            finally
            {
                isAdding.Set(false);
            }
        }

        var kind = RepoPathValidator.Classify(inputValue.Value ?? "");
        object? validationBadge = kind switch
        {
            RepoPathKind.SshUrl => new Badge("SSH").Variant(BadgeVariant.Secondary),
            RepoPathKind.HttpUrl => new Badge("HTTPS").Variant(BadgeVariant.Secondary),
            RepoPathKind.LocalPath => new Badge("Local").Variant(BadgeVariant.Secondary),
            _ => null
        };

        object pickerControls;
        if (isDesktop)
        {
            pickerControls = Layout.Horizontal().Gap(2).Width(Size.Full())
                             | inputValue.ToTextInput("SSH URL, HTTPS URL, or local path")
                                 .Width(Size.Grow())
                             | (validationBadge ?? null!)
                             | new Button("Browse").Icon(Icons.FolderOpen).Outline()
                                 .OnClick(() =>
                                 {
                                     var picked = desktop!.ShowSelectFolderDialog("Select repository folder");
                                     if (picked != null && picked.Length > 0 && !string.IsNullOrEmpty(picked[0]))
                                         inputValue.Set(picked[0]);
                                 })
                             | new Button(isAdding.Value ? "Adding..." : "Add").Icon(Icons.Plus)
                                 .Disabled(string.IsNullOrWhiteSpace(inputValue.Value) || isAdding.Value)
                                 .OnClick(() => { _ = AddAsync(); });
        }
        else
        {
            pickerControls = Layout.Horizontal().Gap(2).Width(Size.Full())
                             | inputValue.ToTextInput("SSH URL, HTTPS URL, or local path")
                                 .Width(Size.Grow())
                             | (validationBadge ?? null!)
                             | new Button(isAdding.Value ? "Adding..." : "Add").Icon(Icons.Plus)
                                 .Disabled(string.IsNullOrWhiteSpace(inputValue.Value) || isAdding.Value)
                                 .OnClick(() => { _ = AddAsync(); });
        }

        var listLayout = Layout.Vertical().Gap(2);
        var current = repos.Value;
        for (var i = 0; i < current.Count; i++)
        {
            var idx = i;
            var item = current[idx];
            var itemKind = RepoPathValidator.Classify(item.Path);
            var isLocal = itemKind == RepoPathKind.LocalPath;

            object? validityIcon = null;
            object pathLabel = Text.Block(GetDisplayLabel(item));
            if (isLocal)
            {
                var expanded = VariableExpansion.ExpandVariables(item.Path, tendrilHome);
                var pathExists = Directory.Exists(expanded);
                var isGitRepo = pathExists && Path.Exists(Path.Combine(expanded, ".git"));
                if (!isGitRepo)
                {
                    pathLabel = Text.Block(item.Path).Color(Colors.Destructive);
                    validityIcon = new Icon(Icons.TriangleAlert, Colors.Warning).Small()
                        .WithTooltip(!pathExists
                            ? $"Path does not exist: {expanded}"
                            : $"Not a git repository: {expanded}");
                }
            }

            var kindBadge = itemKind switch
            {
                RepoPathKind.SshUrl => (object)new Badge("SSH").Variant(BadgeVariant.Outline),
                RepoPathKind.HttpUrl => (object)new Badge("HTTPS").Variant(BadgeVariant.Outline),
                RepoPathKind.LocalPath => (object)new Badge("Local").Variant(BadgeVariant.Outline),
                _ => (object)new Badge("Remote").Variant(BadgeVariant.Outline)
            };

            object row = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.Center).Gap(2)
                         | (validityIcon ?? null!)
                         | pathLabel
                         | kindBadge
                         | new Spacer()
                         | (showPrRule
                             ? (object)BuildPrRuleSelector(repos, idx)
                             : null!)
                         | new Button().Icon(Icons.X).Ghost().OnClick(() =>
                         {
                             var list = new List<RepoRef>(repos.Value);
                             if (idx < list.Count) list.RemoveAt(idx);
                             repos.Set(list);
                         }).WithTooltip("Remove");

            listLayout |= new Box(row).BorderStyle(BorderStyle.None).Background(Colors.Muted).Padding(4, 2, 2, 2).Width(Size.Full());
        }

        return Layout.Vertical().Gap(4).Width(Size.Full())
               | Text.Muted("Add repositories by SSH URL, HTTPS URL, or local path.")
               | (addingError.Value != null ? Text.Danger(addingError.Value) : null!)
               | pickerControls
               | (current.Count > 0 ? new Separator() : null!)
               | (current.Count > 0 ? listLayout : null!);
    }

    private static object BuildPrRuleSelector(IState<List<RepoRef>> repos, int idx)
    {
        var bridge = new ConvertedState<List<RepoRef>, string>(
            repos,
            list => idx < list.Count ? list[idx].PrRule : "default",
            value =>
            {
                var list = new List<RepoRef>(repos.Value);
                if (idx < list.Count)
                    list[idx] = list[idx] with { PrRule = value };
                return list;
            });

        return bridge.ToSelectInput(new List<string> { "default", "yolo" }).Width(Size.Units(20));
    }

    private static string GetDisplayLabel(RepoRef repo)
    {
        var name = RepoPathValidator.ExtractRepoName(repo.Path);
        if (name != null)
        {
            var kind = RepoPathValidator.Classify(repo.Path);
            if (kind == RepoPathKind.HttpUrl || kind == RepoPathKind.SshUrl)
            {
                // Show owner/repo for remote URLs
                var trimmed = repo.Path;
                if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed[..^4];
                if (kind == RepoPathKind.SshUrl)
                {
                    var colonIdx = trimmed.IndexOf(':');
                    if (colonIdx >= 0)
                        return trimmed[(colonIdx + 1)..];
                }
                else
                {
                    var parts = trimmed.Split('/');
                    if (parts.Length >= 2)
                        return $"{parts[^2]}/{parts[^1]}";
                }
            }
            return name;
        }
        return repo.Path;
    }

    private static string SanitizeProjectName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Regex.Replace(input, @"[^A-Za-z0-9._-]", "");
    }
}

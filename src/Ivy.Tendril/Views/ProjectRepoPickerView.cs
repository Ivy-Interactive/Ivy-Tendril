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
    bool showPrRule = false,
    IState<string[]>? preFetchedOwners = null,
    IState<Dictionary<string, string[]>>? preFetchedReposByOwner = null) : ViewBase
{
    public override object Build()
    {
        var mode = UseState("remote"); // "remote" | "local"
        var ownersInternal = UseState<string[]>(Array.Empty<string>);
        var reposByOwnerInternal = UseState<Dictionary<string, string[]>>(() => new Dictionary<string, string[]>());
        var branchesByRepo = UseState<Dictionary<string, string[]>>(() => new Dictionary<string, string[]>());
        var fetchingRepos = UseState(preFetchedOwners == null || preFetchedReposByOwner == null);

        var selectedOwner = UseState("");
        var selectedRepo = UseState("");
        var selectedBranch = UseState("");
        var loadingBranches = UseState(false);

        var newLocalPath = UseState("");
        var addingError = UseState<string?>(null);
        var isAdding = UseState(false);

        UseEffect(async () =>
        {
            if (preFetchedOwners != null && preFetchedReposByOwner != null)
            {
                fetchingRepos.Set(false);
                var pfOwners = preFetchedOwners.Value;
                if (pfOwners.Length > 0 && string.IsNullOrEmpty(selectedOwner.Value))
                    selectedOwner.Set(pfOwners[0]);
                return;
            }

            try
            {
                fetchingRepos.Set(true);
                var ownersList = await GitHubCliHelper.GetOwnersAsync();
                ownersInternal.Set(ownersList);

                var fetches = ownersList.Select(async o => (Owner: o, Repos: await GitHubCliHelper.GetRepositoriesAsync(o)));
                var results = await Task.WhenAll(fetches);
                var byOwner = new Dictionary<string, string[]>();
                foreach (var (owner, ownerRepos) in results)
                    byOwner[owner] = ownerRepos;
                reposByOwnerInternal.Set(byOwner);

                if (ownersList.Length > 0 && string.IsNullOrEmpty(selectedOwner.Value))
                    selectedOwner.Set(ownersList[0]);
            }
            finally
            {
                fetchingRepos.Set(false);
            }
        }, [EffectTrigger.OnMount()]);

        UseEffect(() =>
        {
            selectedRepo.Set("");
            selectedBranch.Set("");
        }, selectedOwner);

        UseEffect(async () =>
        {
            selectedBranch.Set("");
            if (string.IsNullOrEmpty(selectedOwner.Value) || string.IsNullOrEmpty(selectedRepo.Value))
                return;

            var key = $"{selectedOwner.Value}/{selectedRepo.Value}";
            var cache = branchesByRepo.Value;
            if (cache.TryGetValue(key, out var cached))
            {
                if (cached.Length > 0) selectedBranch.Set(cached[0]);
                return;
            }

            loadingBranches.Set(true);
            try
            {
                var fetched = await GitHubCliHelper.GetBranchesAsync(selectedOwner.Value, selectedRepo.Value);
                var next = new Dictionary<string, string[]>(branchesByRepo.Value) { [key] = fetched };
                branchesByRepo.Set(next);
                if (fetched.Length > 0) selectedBranch.Set(fetched[0]);
            }
            finally
            {
                loadingBranches.Set(false);
            }
        }, selectedRepo);

        Context.TryUseService<DesktopWindow>(out var desktop);
        var isDesktop = desktop != null;
        var isRemote = mode.Value == "remote";
        var hasPreFetched = preFetchedOwners != null && preFetchedReposByOwner != null;

        IConfigService? configService = null;
        Context.TryUseService<IConfigService>(out configService);
        var tendrilHome = configService?.TendrilHome;

        var modeToggle = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                         | Text.Block("Remote")
                         | new ConvertedState<string, bool>(
                                 mode,
                                 m => m == "local",
                                 v => v ? "local" : "remote")
                             .ToSwitchInput()
                         | Text.Block("Local");

        async Task AddRemoteAsync()
        {
            var owner = selectedOwner.Value;
            var name = selectedRepo.Value;
            var branch = selectedBranch.Value;
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(name)) return;

            var url = $"https://github.com/{owner}/{name}.git";
            if (repos.Value.Any(r => string.Equals(r.Path, url, StringComparison.OrdinalIgnoreCase)))
            {
                selectedRepo.Set("");
                return;
            }

            var draft = new RepoRef
            {
                Path = url,
                PrRule = "default",
                BaseBranch = string.IsNullOrEmpty(branch) ? null : branch,
            };

            await AppendAsync(draft, name);
            selectedRepo.Set("");
        }

        async Task AddLocalAsync()
        {
            var path = (newLocalPath.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(path)) return;
            if (repos.Value.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                newLocalPath.Set("");
                return;
            }

            var draft = new RepoRef { Path = path, PrRule = "default" };
            var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(leaf)) leaf = path;

            await AppendAsync(draft, leaf);
            newLocalPath.Set("");
        }

        async Task AppendAsync(RepoRef draft, string suggestedName)
        {
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

        var ownersValue = hasPreFetched ? preFetchedOwners!.Value : ownersInternal.Value;
        var reposByOwnerValue = hasPreFetched ? preFetchedReposByOwner!.Value : reposByOwnerInternal.Value;

        object pickerControls;
        if (isRemote)
        {
            if (fetchingRepos.Value)
            {
                pickerControls = Layout.Horizontal().Gap(2).AlignContent(Align.Center).Width(Size.Full()).Padding(4)
                                 | Icons.LoaderCircle.ToIcon().WithAnimation(AnimationType.Rotate).Duration(1)
                                 | Text.Muted("Fetching your GitHub repositories...");
            }
            else
            {
                var ownerRepos = reposByOwnerValue.TryGetValue(selectedOwner.Value, out var r) ? r : Array.Empty<string>();
                var key = $"{selectedOwner.Value}/{selectedRepo.Value}";
                var availableBranches = branchesByRepo.Value.TryGetValue(key, out var b) ? b : Array.Empty<string>();

                pickerControls = Layout.Horizontal().Gap(2).Width(Size.Full())
                                 | selectedOwner.ToSelectInput(ownersValue)
                                     .Placeholder("Owner")
                                     .Width(Size.Fraction(0.25f))
                                 | selectedRepo.ToSelectInput(ownerRepos, disabled: string.IsNullOrEmpty(selectedOwner.Value))
                                     .Placeholder("Repository")
                                     .Width(Size.Grow())
                                 | selectedBranch.ToSelectInput(availableBranches,
                                         disabled: string.IsNullOrEmpty(selectedRepo.Value) || loadingBranches.Value)
                                     .Placeholder(loadingBranches.Value ? "Loading..." : "Branch")
                                     .Width(Size.Fraction(0.25f))
                                 | new Button(isAdding.Value ? "Adding..." : "Add").Icon(Icons.Plus)
                                     .Disabled(string.IsNullOrEmpty(selectedRepo.Value) || isAdding.Value)
                                     .OnClick(() => { _ = AddRemoteAsync(); });
            }
        }
        else if (isDesktop)
        {
            pickerControls = Layout.Horizontal().Gap(2).Width(Size.Full())
                             | newLocalPath.ToTextInput("Repository path")
                                 .Width(Size.Grow())
                             | new Button("Browse").Icon(Icons.FolderOpen).Outline()
                                 .OnClick(() =>
                                 {
                                     var picked = desktop!.ShowSelectFolderDialog("Select repository folder");
                                     if (picked != null && picked.Length > 0 && !string.IsNullOrEmpty(picked[0]))
                                         newLocalPath.Set(picked[0]);
                                 })
                             | new Button(isAdding.Value ? "Adding..." : "Add").Icon(Icons.Plus)
                                 .Disabled(string.IsNullOrWhiteSpace(newLocalPath.Value) || isAdding.Value)
                                 .OnClick(() => { _ = AddLocalAsync(); });
        }
        else
        {
            pickerControls = Layout.Horizontal().Gap(2).Width(Size.Full())
                             | newLocalPath.ToTextInput("Absolute path to local repository (e.g. /Users/you/code/myrepo)")
                                 .Width(Size.Grow())
                             | new Button(isAdding.Value ? "Adding..." : "Add").Icon(Icons.Plus)
                                 .Disabled(string.IsNullOrWhiteSpace(newLocalPath.Value) || isAdding.Value)
                                 .OnClick(() => { _ = AddLocalAsync(); });
        }

        var listLayout = Layout.Vertical().Gap(2);
        var current = repos.Value;
        for (var i = 0; i < current.Count; i++)
        {
            var idx = i;
            var item = current[idx];
            var isLocal = !LooksLikeUrl(item.Path);

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

            object row = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.Center).Gap(2)
                         | (validityIcon ?? null!)
                         | pathLabel
                         | (isLocal
                             ? (object)new Badge("Local").Variant(BadgeVariant.Outline)
                             : new Badge("Remote").Variant(BadgeVariant.Outline))
                         | (!isLocal && !string.IsNullOrEmpty(item.BaseBranch)
                             ? (object)new Badge(item.BaseBranch!).Variant(BadgeVariant.Secondary)
                             : null!)
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

            listLayout |= new Box(row).BorderStyle(BorderStyle.None).Background(Colors.Muted, 0.15f).Padding(4, 2, 2, 2).Width(Size.Full());
        }

        var helperText = isRemote
            ? "Pick the GitHub repositories this project will work with."
            : (isDesktop
                ? "Select folders containing local git repositories."
                : "Add absolute paths to local git repositories on the server.");

        return Layout.Vertical().Gap(4).Width(Size.Full())
               | Text.Muted(helperText)
               | modeToggle
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

    private static bool LooksLikeUrl(string path)
        => !string.IsNullOrEmpty(path)
           && (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("git@", StringComparison.OrdinalIgnoreCase));

    private static string GetDisplayLabel(RepoRef repo)
    {
        if (LooksLikeUrl(repo.Path))
        {
            var trimmed = repo.Path;
            if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^4];
            var parts = trimmed.Split('/');
            if (parts.Length >= 2)
                return $"{parts[^2]}/{parts[^1]}";
            return trimmed;
        }
        return repo.Path;
    }

    private static string SanitizeProjectName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Regex.Replace(input, @"[^A-Za-z0-9._-]", "");
    }
}

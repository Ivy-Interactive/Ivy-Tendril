using Ivy.Core.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Review.Dialogs;

public class CreatePrDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IJobService jobService,
    Action refreshPlans,
    IConfigService config,
    IGithubService githubService,
    IGitService? gitService = null) : ViewBase
{
    private string GetDefaultBaseBranch()
    {
        var repos = selectedPlan?.GetEffectiveRepoPaths(config) ?? [];
        var repoPath = repos.FirstOrDefault();
        var projectConfig = selectedPlan != null ? config.GetProject(selectedPlan.Project) : null;
        var repoRef = projectConfig?.Repos.FirstOrDefault(r =>
            string.Equals(Path.GetFileName(Environment.ExpandEnvironmentVariables(r.Path)),
                Path.GetFileName(repoPath ?? ""), StringComparison.OrdinalIgnoreCase));
        return repoRef?.BaseBranch
            ?? (repoPath != null ? GitHelper.ResolveDefaultBranch(repoPath, config.TendrilHome) : "main");
    }

    private string? GetRepoPath() => selectedPlan?.GetEffectiveRepoPaths(config).FirstOrDefault();

    public override object? Build()
    {
        var defaultGitService = UseService<IGitService>();
        var isCreating = UseState(false);
        var createPrSolveMergeConflicts = UseState(true);
        var createPrMerge = UseState(true);
        var createPrDeleteBranch = UseState(true);
        var createPrIncludeArtifacts = UseState(false);
        var createPrDraft = UseState(false);
        var createPrReviewers = UseState(Array.Empty<string>());
        var createPrComment = UseState("");
        var assigneesError = UseState<string?>(null);
        var selectedBranch = UseState(GetDefaultBaseBranch);
        var isCustomBranch = UseState(false);
        var customBranchText = UseState("");

        var branchesQuery = UseQuery<string[], string>(
            GetRepoPath() ?? "",
            async (path, _) =>
            {
                if (string.IsNullOrEmpty(path)) return [];
                var git = gitService ?? defaultGitService;
                var res = git.GetBranches(path);
                return res.IsSuccess ? res.Value.ToArray() : [];
            },
            initialValue: []
        );

        var assigneesQuery = UseQuery<string[], string>(
            selectedPlan?.Project ?? "",
            async (_, _) =>
            {
                var repoPath = GetRepoPath();
                if (selectedPlan is null || repoPath is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repoConfig = githubService.GetRepoConfigFromPathCached(repoPath);
                if (repoConfig is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var (assignees, error) = await githubService.GetAssigneesAsync(repoConfig.Owner, repoConfig.Name);
                assigneesError.Set(error);
                return assignees.ToArray();
            },
            initialValue: Array.Empty<string>()
        );

        UseEffect(() =>
        {
            if (!createPrMerge.Value) createPrDeleteBranch.Set(false);
        }, createPrMerge);

        if (!dialogOpen.Value) return null;

        var defaultBaseBranch = GetDefaultBaseBranch();
        var multipleBranches = (selectedPlan?.Repos.Count ?? 0) > 1;

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader($"Create PR for #{selectedPlan?.Id}"),
            new DialogBody(
                Layout.Vertical().Gap(2)
                | BuildTargetBranchField(selectedBranch, isCustomBranch, customBranchText, branchesQuery.Value ?? Array.Empty<string>(), defaultBaseBranch)
                | createPrSolveMergeConflicts.ToBoolInput("Solve Merge Conflicts").AutoFocus()
                | createPrMerge.ToBoolInput("Merge")
                | createPrDeleteBranch
                    .ToBoolInput(multipleBranches ? "Delete Branches" : "Delete Branch")
                    .Description(multipleBranches
                        ? "Deletes the branches pushed to origin after successful merge."
                        : "Deletes the branch pushed to origin after successful merge.")
                    .Disabled(!createPrMerge.Value)
                | createPrIncludeArtifacts.ToBoolInput("Include Artifacts")
                | createPrDraft.ToBoolInput("Create as Draft")
                | createPrReviewers.ToSelectInput((assigneesQuery.Value ?? Array.Empty<string>()).ToOptions())
                    .Placeholder("Select reviewers...")
                    .WithField().Label("Reviewers")
                | (assigneesError.Value is { } err
                    ? Text.Danger(err).Small()
                    : null)
                | createPrComment.ToTextareaInput("Comment").Rows(3)
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button("Create PR").Primary().Disabled(isCreating.Value).ShortcutKey("Ctrl+Enter").OnClick(() =>
                {
                    if (!isCreating.Value && selectedPlan != null)
                    {
                        isCreating.Set(true);
                        var targetBaseBranch = isCustomBranch.Value && !string.IsNullOrWhiteSpace(customBranchText.Value)
                            ? customBranchText.Value.Trim()
                            : selectedBranch.Value;

                        jobService.StartJob(new CreatePrArgs(
                            selectedPlan.FolderPath,
                            SolveMergeConflicts: createPrSolveMergeConflicts.Value,
                            Merge: createPrMerge.Value,
                            DeleteBranch: createPrDeleteBranch.Value && createPrMerge.Value,
                            IncludeArtifacts: createPrIncludeArtifacts.Value,
                            Reviewers: createPrReviewers.Value,
                            Comment: string.IsNullOrEmpty(createPrComment.Value) ? null : createPrComment.Value,
                            Draft: createPrDraft.Value,
                            BaseBranch: targetBaseBranch));
                        // Plan transition (and pre-state snapshot) handled by JobService.StartJob.
                        refreshPlans();
                        dialogOpen.Set(false);
                    }
                })
            )
        ).Width(Size.Rem(30));
    }

    public static object BuildTargetBranchField(
        IState<string> selectedBranch,
        IState<bool> isCustomBranch,
        IState<string> customBranchText,
        IReadOnlyList<string> availableBranches,
        string defaultBranch)
    {
        const string customOptionValue = "__custom_branch__";

        if (isCustomBranch.Value)
        {
            return Layout.Vertical().Gap(1)
                | (Layout.Horizontal().AlignContent(Align.Center)
                    | Text.Label("Target Branch")
                    | new Spacer()
                    | new Button("Choose from list").Link().Small().OnClick(() =>
                    {
                        isCustomBranch.Set(false);
                        if (string.IsNullOrWhiteSpace(selectedBranch.Value))
                            selectedBranch.Set(defaultBranch);
                    }))
                | customBranchText.ToTextInput("Enter custom branch name...")
                    .AutoFocus()
                    .WithField()
                    .Description($"Pull request will target this custom branch instead of {defaultBranch}.");
        }

        var options = new List<Option<string>>();
        if (!string.IsNullOrEmpty(defaultBranch))
        {
            options.Add(new Option<string>($"{defaultBranch} (default)", defaultBranch));
        }

        foreach (var b in availableBranches)
        {
            if (!string.Equals(b, defaultBranch, StringComparison.OrdinalIgnoreCase))
            {
                options.Add(new Option<string>(b, b));
            }
        }

        options.Add(new Option<string>("+ Custom branch...", customOptionValue));

        var branchBinding = new ConvertedState<string, string>(
            selectedBranch,
            v => v,
            val =>
            {
                if (val == customOptionValue)
                {
                    isCustomBranch.Set(true);
                    return selectedBranch.Value;
                }
                return val;
            });

        return branchBinding.ToSelectInput(options)
            .Searchable(true)
            .Placeholder("Select target branch...")
            .WithField().Label("Target Branch")
            .Description($"Default: {defaultBranch} (configured in project settings)");
    }
}

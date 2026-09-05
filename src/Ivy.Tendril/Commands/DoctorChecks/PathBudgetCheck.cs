using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal class PathBudgetCheck : IDoctorCheck
{
    private readonly IConfigService _configService;

    public PathBudgetCheck(IConfigService configService)
    {
        _configService = configService;
    }

    public string Name => "Path Budget";

    public async Task<CheckResult> RunAsync()
    {
        var statuses = new List<CheckStatus>();
        var hasErrors = false;

        var plansRoot = _configService.PlansFolder;
        var plansRootLength = plansRoot.Length;
        statuses.Add(new CheckStatus("Plans root", $"{plansRoot} ({plansRootLength} chars)", StatusKind.Ok));

        var longestRepoPath = string.Empty;
        var longestRepoPathLength = 0;

        foreach (var project in _configService.Projects)
        {
            foreach (var repo in project.Repos)
            {
                var relPath = GitHelper.DeriveWorktreeRelativePath(repo);
                if (relPath.Length > longestRepoPathLength)
                {
                    longestRepoPath = relPath;
                    longestRepoPathLength = relPath.Length;
                }
            }
        }

        if (string.IsNullOrEmpty(longestRepoPath))
        {
            statuses.Add(new CheckStatus("Longest repo path", "No repos configured", StatusKind.Warn));
        }
        else
        {
            statuses.Add(new CheckStatus("Longest repo path", $"{longestRepoPath} ({longestRepoPathLength} chars)", StatusKind.Ok));
        }

        var worstCaseLength = WorktreePathHelper.WorstCaseWorktreeRootLength(plansRootLength, longestRepoPathLength);
        var headroom = WorktreePathHelper.MaxWorktreeRootLength - worstCaseLength;
        var classification = Classify(worstCaseLength);

        statuses.Add(new CheckStatus(
            "Worst-case worktree root",
            $"{worstCaseLength} chars (headroom: {headroom})",
            classification));

        if (classification == StatusKind.Error)
        {
            hasErrors = true;
        }

        var legacyCount = 0;
        var longestLegacyFolder = string.Empty;
        var longestLegacyLength = 0;

        if (Directory.Exists(plansRoot))
        {
            var maxAllowedFolderLength = 5 + 1 + PlanYamlHelper.SafeTitleMaxLength;

            foreach (var dir in Directory.GetDirectories(plansRoot))
            {
                var folderName = Path.GetFileName(dir);
                if (folderName.Length > maxAllowedFolderLength)
                {
                    legacyCount++;
                    if (folderName.Length > longestLegacyLength)
                    {
                        longestLegacyFolder = folderName;
                        longestLegacyLength = folderName.Length;
                    }
                }
            }
        }

        if (legacyCount > 0)
        {
            statuses.Add(new CheckStatus(
                "Legacy plan folders over budget",
                $"{legacyCount} folders (longest: {longestLegacyFolder} at {longestLegacyLength} chars)",
                StatusKind.Warn));
        }
        else
        {
            statuses.Add(new CheckStatus("Legacy plan folders over budget", "None", StatusKind.Ok));
        }

        return await Task.FromResult(new CheckResult(hasErrors, statuses));
    }

    internal static StatusKind Classify(int worstCaseRootLength)
    {
        if (worstCaseRootLength <= WorktreePathHelper.MaxWorktreeRootLength)
            return StatusKind.Ok;
        if (worstCaseRootLength <= 125)
            return StatusKind.Warn;
        return StatusKind.Error;
    }
}

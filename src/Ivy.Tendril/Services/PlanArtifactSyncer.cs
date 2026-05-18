using System.Diagnostics;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

internal class PlanArtifactSyncer
{
    private readonly IConfigService? _configService;
    private readonly ILogger _logger;
    private readonly IPlanWatcherService? _planWatcherService;

    internal PlanArtifactSyncer(IConfigService? configService, ILogger logger, IPlanWatcherService? planWatcherService)
    {
        _configService = configService;
        _logger = logger;
        _planWatcherService = planWatcherService;
    }

    internal void SyncPlanArtifacts(JobItem job)
    {
        var planFolder = job.TypedArgs?.PlanFolder ?? "";
        SyncPlanArtifacts(planFolder);
    }

    internal void SyncPlanArtifacts(string planFolder)
    {
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder)) return;

        try
        {
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            var changed = false;

            changed |= SyncVerificationsFromReports(planFolder, plan);
            changed |= SyncCommitsFromWorktrees(planFolder, plan);

            if (changed)
            {
                plan.Updated = DateTime.UtcNow;
                PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcherService);
                _logger.LogInformation(
                    "Synced plan artifacts from disk for {PlanFolder} (agent did not call CLI commands)",
                    Path.GetFileName(planFolder));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync plan artifacts for {PlanFolder}", planFolder);
        }
    }

    private bool SyncVerificationsFromReports(string planFolder, PlanYaml plan)
    {
        var verificationDir = Path.Combine(planFolder, "Verification");
        if (!Directory.Exists(verificationDir)) return false;
        if (plan.Verifications == null || plan.Verifications.Count == 0) return false;

        var changed = false;

        foreach (var reportFile in Directory.GetFiles(verificationDir, "*.md"))
        {
            var reportName = Path.GetFileNameWithoutExtension(reportFile);
            if (reportName.Equals("PreExecution", StringComparison.OrdinalIgnoreCase)) continue;

            var verification = plan.Verifications.FirstOrDefault(v =>
                v.Name.Equals(reportName, StringComparison.OrdinalIgnoreCase));
            if (verification == null || verification.Status != "Pending") continue;

            try
            {
                var content = FileHelper.ReadAllText(reportFile);
                var result = PlanYamlHelper.ParseVerificationResultFromReport(content);
                if (result != null)
                {
                    verification.Status = result;
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read verification report {ReportPath}", reportFile);
            }
        }

        return changed;
    }

    private bool SyncCommitsFromWorktrees(string planFolder, PlanYaml plan)
    {
        var worktreesDir = Path.Combine(planFolder, "Worktrees");
        if (!Directory.Exists(worktreesDir)) return false;

        var planId = PlanYamlHelper.ExtractPlanIdFromFolder(planFolder);
        var safeTitle = PlanYamlHelper.ExtractSafeTitleFromFolder(planFolder);
        if (planId == null || safeTitle == null) return false;

        var branchName = $"tendril/{planId}-{safeTitle}";
        var planRepoNames = new HashSet<string>(
            plan.Repos.Select(r => Path.GetFileName(Environment.ExpandEnvironmentVariables(r))),
            StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var wtDir in IterateWorktrees(worktreesDir, planRepoNames))
        {
            var commits = ExtractCommitsFromWorktree(wtDir, branchName, plan);
            changed |= AddCommitsToPlan(plan, commits);
        }

        return changed;
    }

    private static IEnumerable<string> IterateWorktrees(string worktreesDir, HashSet<string> planRepoNames)
    {
        foreach (var wtDir in Directory.GetDirectories(worktreesDir))
        {
            var wtName = Path.GetFileName(wtDir);
            if (planRepoNames.Count > 0 && !planRepoNames.Contains(wtName))
                continue;

            var gitFile = Path.Combine(wtDir, ".git");
            if (File.Exists(gitFile))
                yield return wtDir;
        }
    }

    private List<string> ExtractCommitsFromWorktree(string wtDir, string branchName, PlanYaml plan)
    {
        var commits = new List<string>();
        try
        {
            var repoRoot = ResolveRepoRootFromWorktree(wtDir);
            if (repoRoot == null) return commits;

            var baseBranch = ResolveBaseBranch(repoRoot, plan);
            var output = RunGitLog(repoRoot, baseBranch, branchName);
            if (output == null) return commits;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var hash = line.Trim();
                if (hash.Length >= 7)
                    commits.Add(hash);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read commits from worktree {Worktree}", wtDir);
        }

        return commits;
    }

    private static string? ResolveRepoRootFromWorktree(string wtDir)
    {
        var gitFile = Path.Combine(wtDir, ".git");
        var gitContent = FileHelper.ReadAllText(gitFile).Trim();
        var gitDirMatch = Regex.Match(gitContent, @"gitdir:\s*(.+)");
        if (!gitDirMatch.Success) return null;

        var gitDir = gitDirMatch.Groups[1].Value.Trim();
        var repoGitDir = Path.GetFullPath(Path.Combine(gitDir, "..", ".."));
        var repoRoot = Path.GetDirectoryName(repoGitDir);
        return repoRoot != null && Directory.Exists(repoRoot) ? repoRoot : null;
    }

    private static string? RunGitLog(string repoRoot, string baseBranch, string branchName)
    {
        var psi = new ProcessStartInfo("git", $"log --format=%H \"{baseBranch}..{branchName}\"")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExitOrKill(10000);
        return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
    }

    private static bool AddCommitsToPlan(PlanYaml plan, IEnumerable<string> commits)
    {
        var changed = false;
        foreach (var commit in commits)
        {
            var alreadyPresent = plan.Commits.Any(existing =>
                existing.StartsWith(commit, StringComparison.OrdinalIgnoreCase) ||
                commit.StartsWith(existing, StringComparison.OrdinalIgnoreCase));

            if (!alreadyPresent)
            {
                plan.Commits.Add(commit);
                changed = true;
            }
        }
        return changed;
    }

    private string ResolveBaseBranch(string repoRoot, PlanYaml plan)
    {
        return TryGetConfiguredBaseBranch(repoRoot, plan) ?? DetectBaseBranch(repoRoot);
    }

    private string? TryGetConfiguredBaseBranch(string repoRoot, PlanYaml plan)
    {
        if (_configService == null) return null;

        if (!string.IsNullOrEmpty(plan.Project))
        {
            var project = _configService.GetProject(plan.Project);
            var repoRef = project?.Repos.FirstOrDefault(r =>
                Path.GetFullPath(r.Path).Equals(Path.GetFullPath(repoRoot), StringComparison.OrdinalIgnoreCase));
            if (repoRef?.BaseBranch is { Length: > 0 } configured)
                return $"origin/{configured}";
        }

        foreach (var proj in _configService.Projects)
        {
            var repoRef = proj.Repos.FirstOrDefault(r =>
                Path.GetFullPath(r.Path).Equals(Path.GetFullPath(repoRoot), StringComparison.OrdinalIgnoreCase));
            if (repoRef?.BaseBranch is { Length: > 0 } found)
                return $"origin/{found}";
        }

        return null;
    }

    private static string DetectBaseBranch(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "symbolic-ref refs/remotes/origin/HEAD")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return "origin/main";
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExitOrKill(5000);

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                return output.Replace("refs/remotes/", "");
        }
        catch
        {
        }

        return "origin/main";
    }
}

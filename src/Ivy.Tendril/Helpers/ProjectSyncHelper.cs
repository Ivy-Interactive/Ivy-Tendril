using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

public record ProjectSyncResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string RepoPath { get; init; } = "";
    public string? BaseBranch { get; init; }
    public string? GitErrorDetails { get; init; }
    public bool CanFixWithAgent { get; init; }
}

public static class ProjectSyncHelper
{
    public static async Task<ProjectSyncResult> SyncRepositoryAsync(
        string repoPath,
        string? baseBranch,
        string? tendrilHome = null)
    {
        return await Task.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(repoPath))
            {
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = "Repository path is empty",
                    RepoPath = repoPath ?? "",
                    CanFixWithAgent = false
                };
            }

            var normalizedPath = RepoPathValidator.Normalize(repoPath);
            var expandedPath = VariableExpansion.ExpandVariables(normalizedPath, tendrilHome);

            if (!Directory.Exists(expandedPath))
            {
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = $"Repository directory does not exist: {expandedPath}",
                    RepoPath = expandedPath,
                    BaseBranch = baseBranch,
                    CanFixWithAgent = false
                };
            }

            var gitPath = Path.Combine(expandedPath, ".git");
            if (!Directory.Exists(gitPath) && !File.Exists(gitPath))
            {
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = $"Not a git repository: {expandedPath}",
                    RepoPath = expandedPath,
                    BaseBranch = baseBranch,
                    CanFixWithAgent = false
                };
            }

            var (remoteExit, remoteOut, remoteErr) = GitHelper.RunGit("remote", expandedPath, 15000);
            if (remoteExit != 0 || string.IsNullOrWhiteSpace(remoteOut))
            {
                var errorMsg = string.IsNullOrWhiteSpace(remoteErr) ? "No remote configured" : remoteErr.Trim();
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = "No remote configured for repository",
                    RepoPath = expandedPath,
                    BaseBranch = baseBranch,
                    GitErrorDetails = errorMsg,
                    CanFixWithAgent = true
                };
            }

            var remotes = remoteOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .ToList();
            var remote = remotes.Contains("origin", StringComparer.OrdinalIgnoreCase)
                ? "origin"
                : remotes.FirstOrDefault() ?? "origin";

            var (fetchExit, fetchOut, fetchErr) = GitHelper.RunGit($"fetch --prune {remote}", expandedPath, 60000);
            if (fetchExit != 0)
            {
                var errDetails = !string.IsNullOrWhiteSpace(fetchErr) ? fetchErr.Trim() : fetchOut.Trim();
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = $"Failed to fetch from remote '{remote}': {errDetails}",
                    RepoPath = expandedPath,
                    BaseBranch = baseBranch,
                    GitErrorDetails = errDetails,
                    CanFixWithAgent = true
                };
            }

            var targetBranch = baseBranch;
            if (string.IsNullOrWhiteSpace(targetBranch))
            {
                targetBranch = await GitHelper.ResolveDefaultBranchAsync(expandedPath, tendrilHome);
            }
            if (string.IsNullOrWhiteSpace(targetBranch))
            {
                targetBranch = "main";
            }

            var (branchExit, branchOut, _) = GitHelper.RunGit("symbolic-ref --short HEAD", expandedPath, 15000);
            if (branchExit != 0)
            {
                var (headExit, headSha, _) = GitHelper.RunGit("rev-parse --short HEAD", expandedPath, 15000);
                var sha = headExit == 0 ? headSha.Trim() : "unknown";
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = $"HEAD is detached at {sha}. Expected base branch '{targetBranch}'",
                    RepoPath = expandedPath,
                    BaseBranch = targetBranch,
                    GitErrorDetails = $"HEAD is detached at {sha}",
                    CanFixWithAgent = true
                };
            }

            var currentBranch = branchOut.Trim();
            if (!string.Equals(currentBranch, targetBranch, StringComparison.Ordinal))
            {
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = $"Repository is on branch '{currentBranch}', expected base branch '{targetBranch}'",
                    RepoPath = expandedPath,
                    BaseBranch = targetBranch,
                    GitErrorDetails = $"Current branch '{currentBranch}' does not match expected base branch '{targetBranch}'",
                    CanFixWithAgent = true
                };
            }

            var (statusExit, statusOut, statusErr) = GitHelper.RunGit("status --porcelain", expandedPath, 15000);
            if (statusExit != 0)
            {
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = $"Failed to check git status: {statusErr.Trim()}",
                    RepoPath = expandedPath,
                    BaseBranch = targetBranch,
                    GitErrorDetails = statusErr.Trim(),
                    CanFixWithAgent = true
                };
            }

            if (!string.IsNullOrWhiteSpace(statusOut))
            {
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = "Repository has uncommitted or untracked changes",
                    RepoPath = expandedPath,
                    BaseBranch = targetBranch,
                    GitErrorDetails = statusOut.Trim(),
                    CanFixWithAgent = true
                };
            }

            var remoteRef = $"{remote}/{targetBranch}";
            var (revExit, _, _) = GitHelper.RunGit($"rev-parse --verify \"refs/remotes/{remoteRef}\"", expandedPath, 15000);
            if (revExit != 0)
            {
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = $"Remote tracking branch '{remoteRef}' not found after fetch",
                    RepoPath = expandedPath,
                    BaseBranch = targetBranch,
                    GitErrorDetails = $"Remote tracking branch '{remoteRef}' does not exist on '{remote}'",
                    CanFixWithAgent = true
                };
            }

            var (headBeforeExit, headBeforeSha, _) = GitHelper.RunGit("rev-parse HEAD", expandedPath, 15000);
            var (mergeExit, mergeOut, mergeErr) = GitHelper.RunGit($"merge --ff-only {remoteRef}", expandedPath, 30000);
            if (mergeExit != 0)
            {
                var errDetails = !string.IsNullOrWhiteSpace(mergeErr) ? mergeErr.Trim() : mergeOut.Trim();
                return new ProjectSyncResult
                {
                    Success = false,
                    Message = $"Fast-forward merge failed for {remoteRef}",
                    RepoPath = expandedPath,
                    BaseBranch = targetBranch,
                    GitErrorDetails = errDetails,
                    CanFixWithAgent = true
                };
            }

            var (headAfterExit, headAfterSha, _) = GitHelper.RunGit("rev-parse HEAD", expandedPath, 15000);
            var wasUpdated = headBeforeExit == 0 && headAfterExit == 0 &&
                             !string.Equals(headBeforeSha.Trim(), headAfterSha.Trim(), StringComparison.Ordinal);

            var message = wasUpdated
                ? $"Fast-forwarded {targetBranch} to {remoteRef}."
                : "Already up to date.";

            return new ProjectSyncResult
            {
                Success = true,
                Message = message,
                RepoPath = expandedPath,
                BaseBranch = targetBranch,
                CanFixWithAgent = false
            };
        });
    }

    public static async Task<List<ProjectSyncResult>> SyncProjectAsync(
        ProjectConfig project,
        string? specificRepoPath = null,
        string? tendrilHome = null)
    {
        var repos = project.Repos;
        if (!string.IsNullOrWhiteSpace(specificRepoPath))
        {
            var normalizedSpecific = RepoPathValidator.Normalize(specificRepoPath);
            var expandedSpecific = VariableExpansion.ExpandVariables(normalizedSpecific, tendrilHome);
            repos = repos.Where(r =>
            {
                var norm = RepoPathValidator.Normalize(r.Path);
                var exp = VariableExpansion.ExpandVariables(norm, tendrilHome);
                return string.Equals(exp, expandedSpecific, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(norm, normalizedSpecific, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Path.GetFileName(exp.TrimEnd('/', '\\')), specificRepoPath, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(r.Path, specificRepoPath, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        var results = new List<ProjectSyncResult>();
        foreach (var repo in repos)
        {
            var res = await SyncRepositoryAsync(repo.Path, repo.BaseBranch, tendrilHome);
            results.Add(res);
        }

        return results;
    }

    public static string GenerateDiagnosticPrompt(string repoPath, string? baseBranch, string? gitErrorDetails)
    {
        var branch = string.IsNullOrWhiteSpace(baseBranch) ? "default branch" : baseBranch;
        var details = string.IsNullOrWhiteSpace(gitErrorDetails) ? "Unknown error" : gitErrorDetails.Trim();
        return $@"The repository at '{repoPath}' could not be safely synchronized with remote branch '{branch}'.
Issue details:
{details}

Please inspect the repository status, check for uncommitted changes or branch divergence, and help reconcile or update the branch safely without losing any work.";
    }

    public static string GenerateDiagnosticPrompt(ProjectSyncResult result) =>
        GenerateDiagnosticPrompt(result.RepoPath, result.BaseBranch, result.GitErrorDetails);
}

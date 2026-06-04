using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Git;

public class GitService : IGitService
{
    private readonly int _timeoutMs;
    private readonly ILogger<GitService> _logger;

    public GitService(IConfigService config, ILogger<GitService> logger)
    {
        _timeoutMs = config.Settings.GitTimeout * 1000;
        _logger = logger;
    }

    public GitResult<string> GetCommitTitle(string repoPath, string commitHash)
    {
        return ExecuteGitCommand(repoPath, $"log -1 --format=%s {commitHash}",
            output => output.Split('\n', 2)[0]);
    }

    public GitResult<string> GetCommitDiff(string repoPath, string commitHash)
    {
        return ExecuteGitCommand(repoPath, $"show --format=\"\" --patch {commitHash}",
            output => output);
    }

    public GitResult<int> GetCommitFileCount(string repoPath, string commitHash)
    {
        return ExecuteGitCommand(repoPath, $"diff-tree --no-commit-id --name-only -r {commitHash}",
            output => output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    public GitResult<List<(string Status, string FilePath)>> GetCommitFiles(string repoPath, string commitHash)
    {
        return ExecuteGitCommand(repoPath, $"diff-tree --no-commit-id --name-status -r {commitHash}",
            ParseNameStatusOutput);
    }

    public GitResult<string> GetCombinedDiff(string repoPath, string firstCommit, string lastCommit)
    {
        return ExecuteGitCommand(repoPath, $"diff {firstCommit}^..{lastCommit}",
            output => output);
    }

    public GitResult<List<(string Status, string FilePath)>> GetCombinedChangedFiles(string repoPath, string firstCommit, string lastCommit)
    {
        return ExecuteGitCommand(repoPath, $"diff --name-status {firstCommit}^..{lastCommit}",
            ParseNameStatusOutput);
    }

    public GitResult<Dictionary<string, (string Title, int FileCount)>> GetCommitSummaries(string repoPath, IEnumerable<string> commitHashes)
    {
        var hashes = commitHashes.ToList();
        if (hashes.Count == 0)
            return GitResult<Dictionary<string, (string Title, int FileCount)>>.Success(new Dictionary<string, (string, int)>());

        return ExecuteGitCommandWithStdin(repoPath, "log --stdin --no-walk --format=%H%x00%s --numstat", hashes,
            output => ParseCommitSummaries(output, hashes));
    }

    public GitResult<List<WorktreeInfo>> GetWorktrees(string repoPath)
    {
        return ExecuteGitCommand(repoPath, "worktree list --porcelain", ParseWorktreeOutput);
    }

    public GitResult<bool> HasUncommittedChanges(string repoPath)
    {
        return ExecuteGitCommand(repoPath, "status --porcelain",
            output => !string.IsNullOrWhiteSpace(output));
    }

    public GitResult<DirtyRepoResult> GetRepoDirtyState(string repoPath, string expectedBaseBranch)
    {
        try
        {
            if (!Directory.Exists(repoPath))
                return GitResult<DirtyRepoResult>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");

            var gitDir = FindGitDir(repoPath);
            if (gitDir == null)
                return GitResult<DirtyRepoResult>.Failure(GitError.InvalidRepoPath, $"Not a git repository: {repoPath}");

            var reasons = new List<DirtyReasonDetail>();

            CheckBranchState(repoPath, expectedBaseBranch, reasons);
            var hasRemote = CheckRemoteConfigured(repoPath, reasons);
            if (hasRemote)
                CheckAheadOfOrigin(repoPath, expectedBaseBranch, reasons);
            CheckUncommittedChanges(repoPath, reasons);
            CheckUntrackedFiles(repoPath, reasons);
            CheckInProgressOperations(gitDir, reasons);

            return GitResult<DirtyRepoResult>.Success(new DirtyRepoResult { Reasons = reasons });
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<DirtyRepoResult>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error checking dirty state");
            return GitResult<DirtyRepoResult>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    private void CheckBranchState(string repoPath, string expectedBaseBranch, List<DirtyReasonDetail> reasons)
    {
        var (exitCode, output) = RunGitCommand(repoPath, "symbolic-ref --short HEAD");
        if (exitCode != 0)
        {
            var (_, headSha) = RunGitCommand(repoPath, "rev-parse --short HEAD");
            reasons.Add(new DirtyReasonDetail
            {
                Reason = DirtyReason.DetachedHead,
                Message = $"HEAD is detached at {headSha.Trim()}"
            });
        }
        else
        {
            var currentBranch = output.Trim();
            if (!string.Equals(currentBranch, expectedBaseBranch, StringComparison.Ordinal))
            {
                reasons.Add(new DirtyReasonDetail
                {
                    Reason = DirtyReason.NotOnExpectedBranch,
                    Message = $"On branch {currentBranch}, expected {expectedBaseBranch}"
                });
            }
        }
    }

    private bool CheckRemoteConfigured(string repoPath, List<DirtyReasonDetail> reasons)
    {
        var (exitCode, output) = RunGitCommand(repoPath, "remote");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            reasons.Add(new DirtyReasonDetail
            {
                Reason = DirtyReason.NoRemoteConfigured,
                Message = "No remote configured"
            });
            return false;
        }
        return true;
    }

    private void CheckAheadOfOrigin(string repoPath, string expectedBaseBranch, List<DirtyReasonDetail> reasons)
    {
        var (exitCode, output) = RunGitCommand(repoPath, $"log origin/{expectedBaseBranch}..HEAD --oneline");
        if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            var commits = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
            if (commits.Count > 0)
            {
                reasons.Add(new DirtyReasonDetail
                {
                    Reason = DirtyReason.AheadOfOrigin,
                    Message = $"{commits.Count} commit(s) ahead of origin/{expectedBaseBranch}",
                    Files = commits
                });
            }
        }
        else if (exitCode != 0)
        {
            reasons.Add(new DirtyReasonDetail
            {
                Reason = DirtyReason.AheadOfOrigin,
                Message = $"Could not determine status relative to origin/{expectedBaseBranch}"
            });
        }
    }

    private void CheckUncommittedChanges(string repoPath, List<DirtyReasonDetail> reasons)
    {
        var (exitCode, output) = RunGitCommand(repoPath, "status --porcelain");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return;

        var tracked = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Length >= 2 && l[0] != '?' && l[1] != '?')
            .Select(l => l.Length > 3 ? l.Substring(3).Trim() : l.Trim())
            .Where(f => f.Length > 0)
            .ToList();

        if (tracked.Count > 0)
        {
            reasons.Add(new DirtyReasonDetail
            {
                Reason = DirtyReason.UncommittedChanges,
                Message = $"{tracked.Count} file(s) with uncommitted changes",
                Files = tracked
            });
        }
    }

    private void CheckUntrackedFiles(string repoPath, List<DirtyReasonDetail> reasons)
    {
        var (exitCode, output) = RunGitCommand(repoPath, "ls-files --others --exclude-standard");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return;

        var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (files.Count > 0)
        {
            reasons.Add(new DirtyReasonDetail
            {
                Reason = DirtyReason.UntrackedFiles,
                Message = $"{files.Count} untracked file(s)",
                Files = files
            });
        }
    }

    private static void CheckInProgressOperations(string gitDir, List<DirtyReasonDetail> reasons)
    {
        var markers = new (string Path, bool IsDir, string Operation)[]
        {
            ("MERGE_HEAD", false, "merge"),
            ("REBASE_HEAD", false, "rebase"),
            ("CHERRY_PICK_HEAD", false, "cherry-pick"),
            ("BISECT_LOG", false, "bisect"),
            ("rebase-merge", true, "rebase"),
            ("rebase-apply", true, "rebase"),
        };

        foreach (var (path, isDir, op) in markers)
        {
            var fullPath = Path.Combine(gitDir, path);
            if (isDir ? Directory.Exists(fullPath) : File.Exists(fullPath))
            {
                reasons.Add(new DirtyReasonDetail
                {
                    Reason = DirtyReason.InProgressOperation,
                    Message = $"{op} in progress"
                });
                return;
            }
        }
    }

    public GitResult<List<string>> GetReachableCommits(string repoPath, IEnumerable<string> candidateHashes)
    {
        try
        {
            if (!Directory.Exists(repoPath))
                return GitResult<List<string>>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");

            var hashes = candidateHashes.ToList();
            if (hashes.Count == 0)
                return GitResult<List<string>>.Success(new List<string>());

            var reachable = new List<string>();
            foreach (var hash in hashes)
            {
                var (exitCode, _) = RunGitCommand(repoPath, $"merge-base --is-ancestor {hash} HEAD");
                if (exitCode == 0)
                    reachable.Add(hash);
            }

            return GitResult<List<string>>.Success(reachable);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<List<string>>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<List<string>>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    // --- Infrastructure ---

    private GitResult<T> ExecuteGitCommand<T>(string repoPath, string args, Func<string, T> parseOutput)
    {
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<T>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var (exitCode, output) = RunGitCommand(repoPath, args);

            if (exitCode == -1)
            {
                _logger.LogWarning("Failed to run git command or timed out: {Args}", args);
                return GitResult<T>.Failure(GitError.GitNotFound, "Failed to start git process or timed out");
            }

            if (exitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", exitCode);
                return GitResult<T>.Failure(GitError.CommandFailed, $"Git command failed with exit code {exitCode}");
            }

            return GitResult<T>.Success(parseOutput(output));
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<T>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<T>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    private GitResult<T> ExecuteGitCommandWithStdin<T>(string repoPath, string args, List<string> stdinLines, Func<string, T> parseOutput)
    {
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<T>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process");
                return GitResult<T>.Failure(GitError.GitNotFound, "Failed to start git process");
            }

            foreach (var line in stdinLines)
                process.StandardInput.WriteLine(line);
            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEnd();
            var timedOut = !process.WaitForExit(_timeoutMs);

            if (timedOut)
            {
                try { process.Kill(); } catch { }
                _logger.LogWarning("Git command timed out after {Timeout}ms", _timeoutMs);
                return GitResult<T>.Failure(GitError.Timeout, $"Git command timed out after {_timeoutMs}ms");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", process.ExitCode);
                return GitResult<T>.Failure(GitError.CommandFailed, $"Git command failed with exit code {process.ExitCode}");
            }

            return GitResult<T>.Success(parseOutput(output));
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<T>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<T>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    private (int ExitCode, string Output) RunGitCommand(string repoPath, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "");

        var output = process.StandardOutput.ReadToEnd();
        var timedOut = !process.WaitForExit(_timeoutMs);
        if (timedOut)
        {
            try { process.Kill(); } catch { }
            return (-1, "");
        }

        return (process.ExitCode, output);
    }

    private string? FindGitDir(string repoPath)
    {
        var dotGit = Path.Combine(repoPath, ".git");
        if (Directory.Exists(dotGit)) return dotGit;
        if (File.Exists(dotGit))
        {
            var content = File.ReadAllText(dotGit).Trim();
            if (content.StartsWith("gitdir: "))
                return content.Substring(8).Trim();
        }
        return null;
    }

    // --- Parsers ---

    private static List<(string Status, string FilePath)> ParseNameStatusOutput(string output)
    {
        var files = new List<(string Status, string FilePath)>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2)
                files.Add((parts[0].Trim(), parts[1].Trim()));
        }
        return files;
    }

    private static Dictionary<string, (string Title, int FileCount)> ParseCommitSummaries(string output, List<string> hashes)
    {
        var result = new Dictionary<string, (string Title, int FileCount)>();
        var inputHashSet = new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);

        string? currentHash = null;
        string? currentTitle = null;
        int currentFileCount = 0;

        foreach (var line in output.Split('\n'))
        {
            if (line.Contains('\0'))
            {
                if (currentHash != null)
                    StoreCommitResult(result, inputHashSet, currentHash, currentTitle!, currentFileCount);

                var parts = line.Split('\0', 2);
                currentHash = parts[0].Trim();
                currentTitle = parts.Length > 1 ? parts[1].Trim() : "";
                currentFileCount = 0;
            }
            else if (currentHash != null && line.Trim().Length > 0)
            {
                currentFileCount++;
            }
        }

        if (currentHash != null)
            StoreCommitResult(result, inputHashSet, currentHash, currentTitle!, currentFileCount);

        return result;
    }

    private static void StoreCommitResult(
        Dictionary<string, (string Title, int FileCount)> result,
        HashSet<string> inputHashes,
        string fullHash,
        string title,
        int fileCount)
    {
        var value = (title, fileCount);
        result[fullHash] = value;

        foreach (var input in inputHashes)
        {
            if (input.Length < fullHash.Length &&
                fullHash.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                result[input] = value;
            }
        }
    }

    private static List<WorktreeInfo> ParseWorktreeOutput(string output)
    {
        var worktrees = new List<WorktreeInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string? currentPath = null;
        string? currentBranch = null;
        string? currentHash = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("worktree "))
            {
                if (currentPath != null && currentBranch != null && currentHash != null)
                    worktrees.Add(new WorktreeInfo(currentPath, currentBranch, currentHash));

                currentPath = line.Substring(9).Trim();
                currentBranch = null;
                currentHash = null;
            }
            else if (line.StartsWith("HEAD "))
            {
                currentHash = line.Substring(5).Trim();
            }
            else if (line.StartsWith("branch "))
            {
                var branchRef = line.Substring(7).Trim();
                currentBranch = branchRef.Replace("refs/heads/", "");
            }
        }

        if (currentPath != null && currentBranch != null && currentHash != null)
            worktrees.Add(new WorktreeInfo(currentPath, currentBranch, currentHash));

        return worktrees;
    }
}

using System.Diagnostics;
using System.Text;
using Ivy.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

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
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<string>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var psi = new ProcessStartInfo("git", $"log -1 --format=%s {commitHash}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process");
                return GitResult<string>.Failure(GitError.GitNotFound, "Failed to start git process");
            }

            var title = process.StandardOutput.ReadLine();
            var timedOut = !process.WaitForExit(_timeoutMs);

            if (timedOut)
            {
                process.Kill();
                _logger.LogWarning("Git command timed out after {Timeout}ms", _timeoutMs);
                return GitResult<string>.Failure(GitError.Timeout, $"Git command timed out after {_timeoutMs}ms");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", process.ExitCode);
                return GitResult<string>.Failure(GitError.CommandFailed, $"Git command failed with exit code {process.ExitCode}");
            }

            return GitResult<string>.Success(title ?? string.Empty);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<string>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<string>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    public GitResult<string> GetCommitDiff(string repoPath, string commitHash)
    {
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<string>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var psi = new ProcessStartInfo("git", $"show --format=\"\" --patch {commitHash}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process");
                return GitResult<string>.Failure(GitError.GitNotFound, "Failed to start git process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var timedOut = !process.WaitForExit(_timeoutMs);

            if (timedOut)
            {
                process.Kill();
                _logger.LogWarning("Git command timed out after {Timeout}ms", _timeoutMs);
                return GitResult<string>.Failure(GitError.Timeout, $"Git command timed out after {_timeoutMs}ms");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", process.ExitCode);
                return GitResult<string>.Failure(GitError.CommandFailed, $"Git command failed with exit code {process.ExitCode}");
            }

            return GitResult<string>.Success(output ?? string.Empty);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<string>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<string>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    public GitResult<int> GetCommitFileCount(string repoPath, string commitHash)
    {
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<int>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var psi = new ProcessStartInfo("git", $"diff-tree --no-commit-id --name-only -r {commitHash}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process");
                return GitResult<int>.Failure(GitError.GitNotFound, "Failed to start git process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var timedOut = !process.WaitForExit(_timeoutMs);

            if (timedOut)
            {
                process.Kill();
                _logger.LogWarning("Git command timed out after {Timeout}ms", _timeoutMs);
                return GitResult<int>.Failure(GitError.Timeout, $"Git command timed out after {_timeoutMs}ms");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", process.ExitCode);
                return GitResult<int>.Failure(GitError.CommandFailed, $"Git command failed with exit code {process.ExitCode}");
            }

            var count = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            return GitResult<int>.Success(count);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<int>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<int>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    public GitResult<List<(string Status, string FilePath)>> GetCommitFiles(string repoPath, string commitHash)
    {
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var psi = new ProcessStartInfo("git", $"diff-tree --no-commit-id --name-status -r {commitHash}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process");
                return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.GitNotFound, "Failed to start git process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var timedOut = !process.WaitForExit(_timeoutMs);

            if (timedOut)
            {
                process.Kill();
                _logger.LogWarning("Git command timed out after {Timeout}ms", _timeoutMs);
                return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.Timeout, $"Git command timed out after {_timeoutMs}ms");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", process.ExitCode);
                return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.CommandFailed, $"Git command failed with exit code {process.ExitCode}");
            }

            var files = new List<(string Status, string FilePath)>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length == 2)
                    files.Add((parts[0].Trim(), parts[1].Trim()));
            }

            return GitResult<List<(string Status, string FilePath)>>.Success(files);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    public GitResult<string> GetCombinedDiff(string repoPath, string firstCommit, string lastCommit)
    {
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<string>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var psi = new ProcessStartInfo("git", $"diff {firstCommit}^..{lastCommit}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process");
                return GitResult<string>.Failure(GitError.GitNotFound, "Failed to start git process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var timedOut = !process.WaitForExit(_timeoutMs);

            if (timedOut)
            {
                process.Kill();
                _logger.LogWarning("Git command timed out after {Timeout}ms", _timeoutMs);
                return GitResult<string>.Failure(GitError.Timeout, $"Git command timed out after {_timeoutMs}ms");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", process.ExitCode);
                return GitResult<string>.Failure(GitError.CommandFailed, $"Git command failed with exit code {process.ExitCode}");
            }

            return GitResult<string>.Success(output ?? string.Empty);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<string>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<string>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    public GitResult<List<(string Status, string FilePath)>> GetCombinedChangedFiles(string repoPath, string firstCommit, string lastCommit)
    {
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var psi = new ProcessStartInfo("git", $"diff --name-status {firstCommit}^..{lastCommit}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process");
                return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.GitNotFound, "Failed to start git process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var timedOut = !process.WaitForExit(_timeoutMs);

            if (timedOut)
            {
                process.Kill();
                _logger.LogWarning("Git command timed out after {Timeout}ms", _timeoutMs);
                return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.Timeout, $"Git command timed out after {_timeoutMs}ms");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", process.ExitCode);
                return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.CommandFailed, $"Git command failed with exit code {process.ExitCode}");
            }

            var files = new List<(string Status, string FilePath)>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length == 2)
                    files.Add((parts[0].Trim(), parts[1].Trim()));
            }

            return GitResult<List<(string Status, string FilePath)>>.Success(files);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<List<(string Status, string FilePath)>>.Failure(GitError.UnknownError, ex.Message);
        }
    }

    public GitResult<Dictionary<string, (string Title, int FileCount)>> GetCommitSummaries(string repoPath, IEnumerable<string> commitHashes)
    {
        var hashes = commitHashes.ToList();
        if (hashes.Count == 0)
            return GitResult<Dictionary<string, (string Title, int FileCount)>>.Success(new Dictionary<string, (string, int)>());

        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<Dictionary<string, (string Title, int FileCount)>>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var result = new Dictionary<string, (string Title, int FileCount)>();

            // Single git log call: --stdin reads hashes from stdin, --format outputs hash + title, --numstat gives file counts
            var psi = new ProcessStartInfo("git", "log --stdin --no-walk --format=%H%x00%s --numstat")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process");
                return GitResult<Dictionary<string, (string Title, int FileCount)>>.Failure(GitError.GitNotFound, "Failed to start git process");
            }

            foreach (var hash in hashes)
                process.StandardInput.WriteLine(hash);
            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEnd();
            var timedOut = !process.WaitForExit(_timeoutMs);

            if (timedOut)
            {
                process.Kill();
                _logger.LogWarning("Git command timed out after {Timeout}ms", _timeoutMs);
                return GitResult<Dictionary<string, (string Title, int FileCount)>>.Failure(GitError.Timeout, $"Git command timed out after {_timeoutMs}ms");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", process.ExitCode);
                return GitResult<Dictionary<string, (string Title, int FileCount)>>.Failure(GitError.CommandFailed, $"Git command failed with exit code {process.ExitCode}");
            }

            // Build a lookup from full hash prefix back to the original input hashes
            var inputHashSet = new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);

            // Parse: each commit block starts with "hash\0title" followed by numstat lines, separated by empty lines
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

            return GitResult<Dictionary<string, (string Title, int FileCount)>>.Success(result);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<Dictionary<string, (string Title, int FileCount)>>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<Dictionary<string, (string Title, int FileCount)>>.Failure(GitError.UnknownError, ex.Message);
        }
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

        // Also store under any abbreviated input hash that matches this full hash
        foreach (var input in inputHashes)
        {
            if (input.Length < fullHash.Length &&
                fullHash.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                result[input] = value;
            }
        }
    }

    public GitResult<List<WorktreeInfo>> GetWorktrees(string repoPath)
    {
        try
        {
            if (!Directory.Exists(repoPath))
            {
                _logger.LogWarning("Invalid repository path: {RepoPath}", repoPath);
                return GitResult<List<WorktreeInfo>>.Failure(GitError.InvalidRepoPath, $"Repository path does not exist: {repoPath}");
            }

            var psi = new ProcessStartInfo("git", "worktree list --porcelain")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("Failed to start git process");
                return GitResult<List<WorktreeInfo>>.Failure(GitError.GitNotFound, "Failed to start git process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var timedOut = !process.WaitForExit(_timeoutMs);

            if (timedOut)
            {
                process.Kill();
                _logger.LogWarning("Git command timed out after {Timeout}ms", _timeoutMs);
                return GitResult<List<WorktreeInfo>>.Failure(GitError.Timeout, $"Git command timed out after {_timeoutMs}ms");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Git command failed with exit code {ExitCode}", process.ExitCode);
                return GitResult<List<WorktreeInfo>>.Failure(GitError.CommandFailed, $"Git command failed with exit code {process.ExitCode}");
            }

            var worktrees = new List<WorktreeInfo>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            string? currentPath = null;
            string? currentBranch = null;
            string? currentHash = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("worktree "))
                {
                    // Save previous worktree if complete
                    if (currentPath != null && currentBranch != null && currentHash != null)
                    {
                        worktrees.Add(new WorktreeInfo(currentPath, currentBranch, currentHash));
                    }

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

            // Save last worktree
            if (currentPath != null && currentBranch != null && currentHash != null)
            {
                worktrees.Add(new WorktreeInfo(currentPath, currentBranch, currentHash));
            }

            return GitResult<List<WorktreeInfo>>.Success(worktrees);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Git executable not found");
            return GitResult<List<WorktreeInfo>>.Failure(GitError.GitNotFound, "Git executable not found");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unknown error executing git command");
            return GitResult<List<WorktreeInfo>>.Failure(GitError.UnknownError, ex.Message);
        }
    }
}
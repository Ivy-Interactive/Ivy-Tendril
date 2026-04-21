using System.Diagnostics;
using System.Text;
using Ivy.Helpers;

namespace Ivy.Tendril.Services;

public class GitService : IGitService
{
    private readonly int _timeoutMs;

    public GitService(IConfigService config)
    {
        _timeoutMs = config.Settings.GitTimeout * 1000;
    }

    public string? GetCommitTitle(string repoPath, string commitHash)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"log -1 --format=%s {commitHash}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            var title = process?.StandardOutput.ReadLine();
            process.WaitForExitOrKill(_timeoutMs);
            return process?.ExitCode == 0 ? title : null;
        }
        catch
        {
            return null; /* git may not be installed, or repo path invalid */
        }
    }

    public string? GetCommitDiff(string repoPath, string commitHash)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"show --format=\"\" --patch {commitHash}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd();
            process.WaitForExitOrKill(_timeoutMs);
            return process?.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null; /* git may not be installed, or repo path invalid */
        }
    }

    public int? GetCommitFileCount(string repoPath, string commitHash)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"diff-tree --no-commit-id --name-only -r {commitHash}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd();
            process.WaitForExitOrKill(_timeoutMs);
            if (process?.ExitCode != 0 || output == null) return null;

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        }
        catch
        {
            return null;
        }
    }

    public List<(string Status, string FilePath)>? GetCommitFiles(string repoPath, string commitHash)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"diff-tree --no-commit-id --name-status -r {commitHash}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd();
            process.WaitForExitOrKill(_timeoutMs);
            if (process?.ExitCode != 0 || output == null) return null;

            var files = new List<(string Status, string FilePath)>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length == 2)
                    files.Add((parts[0].Trim(), parts[1].Trim()));
            }

            return files;
        }
        catch
        {
            return null; /* git may not be installed, or repo path invalid */
        }
    }

    public string? GetCombinedDiff(string repoPath, string firstCommit, string lastCommit)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"diff {firstCommit}^..{lastCommit}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd();
            process.WaitForExitOrKill(_timeoutMs);
            return process?.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    public List<(string Status, string FilePath)>? GetCombinedChangedFiles(string repoPath, string firstCommit, string lastCommit)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"diff --name-status {firstCommit}^..{lastCommit}")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd();
            process.WaitForExitOrKill(_timeoutMs);
            if (process?.ExitCode != 0 || output == null) return null;

            var files = new List<(string Status, string FilePath)>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length == 2)
                    files.Add((parts[0].Trim(), parts[1].Trim()));
            }

            return files;
        }
        catch
        {
            return null;
        }
    }

    public List<WorktreeInfo>? GetWorktrees(string repoPath)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "worktree list --porcelain")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd();
            process.WaitForExitOrKill(_timeoutMs);
            if (process?.ExitCode != 0 || output == null) return null;

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

            return worktrees;
        }
        catch
        {
            return null; /* git may not be installed, or repo path invalid */
        }
    }
}
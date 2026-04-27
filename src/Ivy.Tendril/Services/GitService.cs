using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Helpers;

namespace Ivy.Tendril.Services;

public class GitService : IGitService
{
    private readonly int _timeoutMs;

    public GitService(IConfigService config)
    {
        _timeoutMs = config.Settings.GitTimeout * 1000;
    }

    public static bool IsValidCommitHash(string? hash)
    {
        return !string.IsNullOrEmpty(hash) &&
               Regex.IsMatch(hash, "^[a-fA-F0-9]{7,40}$");
    }

    private T? RunGitCommand<T>(string repoPath, string args, Func<string, T?> parser)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
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
            return process?.ExitCode == 0 ? parser(output ?? "") : default;
        }
        catch
        {
            return default;
        }
    }

    public string? GetCommitTitle(string repoPath, string commitHash)
    {
        if (!IsValidCommitHash(commitHash))
            return null;

        return RunGitCommand(repoPath, $"log -1 --format=%s -- {commitHash}",
            output => output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
    }

    public string? GetCommitDiff(string repoPath, string commitHash)
    {
        if (!IsValidCommitHash(commitHash))
            return null;

        return RunGitCommand(repoPath, $"show --format=\"\" --patch -- {commitHash}", output => output);
    }

    public int? GetCommitFileCount(string repoPath, string commitHash)
    {
        if (!IsValidCommitHash(commitHash))
            return null;

        return RunGitCommand(repoPath, $"diff-tree --no-commit-id --name-only -r -- {commitHash}",
            output => output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    public List<(string Status, string FilePath)>? GetCommitFiles(string repoPath, string commitHash)
    {
        if (!IsValidCommitHash(commitHash))
            return null;

        return RunGitCommand(repoPath, $"diff-tree --no-commit-id --name-status -r -- {commitHash}",
            GitOutputParser.ParseNameStatusOutput);
    }

    public string? GetCombinedDiff(string repoPath, string firstCommit, string lastCommit)
    {
        if (!IsValidCommitHash(firstCommit) || !IsValidCommitHash(lastCommit))
            return null;

        return RunGitCommand(repoPath, $"diff -- {firstCommit}^..{lastCommit}", output => output);
    }

    public List<(string Status, string FilePath)>? GetCombinedChangedFiles(string repoPath, string firstCommit, string lastCommit)
    {
        if (!IsValidCommitHash(firstCommit) || !IsValidCommitHash(lastCommit))
            return null;

        return RunGitCommand(repoPath, $"diff --name-status -- {firstCommit}^..{lastCommit}",
            GitOutputParser.ParseNameStatusOutput);
    }

    public Dictionary<string, (string Title, int FileCount)>? GetCommitSummaries(string repoPath, IEnumerable<string> commitHashes)
    {
        var hashes = commitHashes.ToList();
        if (hashes.Count == 0) return new Dictionary<string, (string, int)>();

        // Validate all hashes first
        if (hashes.Any(hash => !IsValidCommitHash(hash)))
            return null;

        try
        {
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
            if (process == null) return null;

            foreach (var hash in hashes)
                process.StandardInput.WriteLine(hash);
            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExitOrKill(_timeoutMs);
            if (process.ExitCode != 0) return null;

            var inputHashSet = new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);
            return GitOutputParser.ParseCommitSummaries(output, inputHashSet);
        }
        catch
        {
            return null;
        }
    }

    public List<WorktreeInfo>? GetWorktrees(string repoPath)
        => RunGitCommand(repoPath, "worktree list --porcelain", GitOutputParser.ParseWorktreeList);
}
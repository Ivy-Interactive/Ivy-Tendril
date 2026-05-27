using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ivy.Tendril.Helpers;

public static class GitHelper
{
    public static string? ResolveRepoRootFromWorktree(string wtDir)
    {
        var gitFile = Path.Combine(wtDir, ".git");
        if (!File.Exists(gitFile)) return null;
        var gitContent = FileHelper.ReadAllText(gitFile).Trim();
        var gitDirMatch = Regex.Match(gitContent, @"gitdir:\s*(.+)");
        if (!gitDirMatch.Success) return null;

        var gitDir = gitDirMatch.Groups[1].Value.Trim();
        var repoGitDir = Path.GetFullPath(Path.Combine(wtDir, gitDir, "..", ".."));
        var repoRoot = Path.GetDirectoryName(repoGitDir);
        return repoRoot != null && Directory.Exists(repoRoot) ? repoRoot : null;
    }

    public static async Task<bool> IsValidBranchAsync(string repoPath, string branchName, string? tendrilHome = null)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || string.IsNullOrWhiteSpace(branchName))
            return false;

        var expandedPath = VariableExpansion.ExpandVariables(repoPath, tendrilHome);
        var kind = RepoPathValidator.Classify(expandedPath);
        if (kind == RepoPathKind.Invalid)
            return false;

        if (kind == RepoPathKind.LocalPath)
        {
            if (!Directory.Exists(expandedPath))
                return false;

            return await Task.Run(() =>
            {
                // Try local branch first: refs/heads/<branchName>
                if (RunGitShowRef(expandedPath, $"refs/heads/{branchName}"))
                    return true;

                // Try remote branch: refs/remotes/origin/<branchName>
                if (RunGitShowRef(expandedPath, $"refs/remotes/origin/{branchName}"))
                    return true;

                // Check other remotes
                try
                {
                    var psi = new ProcessStartInfo("git", "show-ref")
                    {
                        WorkingDirectory = expandedPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return false;
                    
                    var outTask = process.StandardOutput.ReadToEndAsync();
                    var errTask = process.StandardError.ReadToEndAsync();
                    process.WaitForExit(5000);
                    
                    var output = outTask.GetAwaiter().GetResult();
                    _ = errTask.GetAwaiter().GetResult();

                    if (process.ExitCode == 0)
                    {
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var parts = line.Split(' ', 2);
                            if (parts.Length == 2)
                            {
                                var refName = parts[1].Trim();
                                if (refName.Equals($"refs/heads/{branchName}", StringComparison.OrdinalIgnoreCase) ||
                                    (refName.StartsWith("refs/remotes/", StringComparison.OrdinalIgnoreCase) && refName.EndsWith($"/{branchName}", StringComparison.OrdinalIgnoreCase)))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore process execution errors
                }

                return false;
            });
        }
        else // Remote URL: HttpUrl or SshUrl
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("git", $"ls-remote --heads \"{expandedPath}\" \"{branchName}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null) return false;
                    
                    var outTask = process.StandardOutput.ReadToEndAsync();
                    var errTask = process.StandardError.ReadToEndAsync();
                    process.WaitForExit(10000);
                    
                    var output = outTask.GetAwaiter().GetResult();
                    _ = errTask.GetAwaiter().GetResult();

                    if (process.ExitCode == 0)
                    {
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            if (line.Contains($"refs/heads/{branchName}"))
                                return true;
                        }
                    }
                }
                catch
                {
                    // Ignore process execution errors
                }

                return false;
            });
        }
    }

    private static bool RunGitShowRef(string repoPath, string refName)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"show-ref --verify \"{refName}\"")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;
            
            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit(5000);
            
            _ = outTask.GetAwaiter().GetResult();
            _ = errTask.GetAwaiter().GetResult();
            
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

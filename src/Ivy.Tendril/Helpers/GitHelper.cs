using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ivy.Tendril.Helpers;

public static class GitHelper
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly ConcurrentDictionary<string, string> _defaultBranchCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a ProcessStartInfo for git with fsmonitor suppressed. Git spawns a detached
    /// `git fsmonitor--daemon` per repo/worktree when core.fsmonitor is enabled; it outlives the
    /// invocation and escapes both Kill(entireProcessTree) and ChildProcessTracker's job object,
    /// so one leaks per worktree Tendril touches (#1853). Tendril's git usage is short-lived reads
    /// and worktree management, which gain nothing from a persistent watcher.
    /// </summary>
    public static ProcessStartInfo MakeGitStartInfo(string arguments, string? workingDirectory = null)
    {
        return new ProcessStartInfo("git", $"-c core.fsmonitor=false --no-optional-locks {arguments}")
        {
            WorkingDirectory = !string.IsNullOrEmpty(workingDirectory) ? workingDirectory : Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };
    }

    /// <summary>
    /// Resolves a repository's default branch as a bare name (e.g. "development").
    /// Tries the local <c>origin/HEAD</c> symbolic ref first (fast, offline); if that is not set up,
    /// queries the remote via <c>git ls-remote --symref</c>. Falls back to "main" only when neither
    /// source yields an answer. Accepts a local clone path or a remote URL (the URL form works before
    /// the repo is cloned). Successful detections are memoized per expanded path/url.
    /// </summary>
    public static string ResolveDefaultBranch(string repoPathOrUrl, string? tendrilHome = null)
    {
        if (string.IsNullOrWhiteSpace(repoPathOrUrl))
            return "main";

        var expanded = VariableExpansion.ExpandVariables(repoPathOrUrl, tendrilHome);
        if (_defaultBranchCache.TryGetValue(expanded, out var cached))
            return cached;

        var detected = DetectDefaultBranch(expanded);
        if (detected != null)
            _defaultBranchCache[expanded] = detected;
        return detected ?? "main";
    }

    /// <summary>Async wrapper around <see cref="ResolveDefaultBranch"/> for UI call sites.</summary>
    public static Task<string> ResolveDefaultBranchAsync(string repoPathOrUrl, string? tendrilHome = null)
        => Task.Run(() => ResolveDefaultBranch(repoPathOrUrl, tendrilHome));

    private static string? DetectDefaultBranch(string expanded)
    {
        var kind = RepoPathValidator.Classify(expanded);
        if (kind == RepoPathKind.Invalid)
            return null;

        if (kind == RepoPathKind.LocalPath)
        {
            if (!Directory.Exists(expanded))
                return null;

            // 1. Local origin/HEAD symbolic ref (fast, offline). Outputs e.g. "origin/development".
            var local = RunGitCapture(expanded, "symbolic-ref --short refs/remotes/origin/HEAD", 5000);
            if (!string.IsNullOrWhiteSpace(local))
            {
                var name = StripOriginPrefix(local.Trim());
                if (!string.IsNullOrEmpty(name))
                    return name;
            }

            // 2. origin/HEAD not set up locally — ask the remote directly.
            return ParseSymrefHead(RunGitCapture(expanded, "ls-remote --symref origin HEAD", 10000));
        }

        // Remote URL (HttpUrl or SshUrl): query the remote directly, no clone required.
        return ParseSymrefHead(RunGitCapture(null, $"ls-remote --symref \"{expanded}\" HEAD", 10000));
    }

    private static string StripOriginPrefix(string refName)
        => refName.StartsWith("origin/", StringComparison.Ordinal) ? refName["origin/".Length..] : refName;

    private static string? ParseSymrefHead(string? lsRemoteOutput)
    {
        if (string.IsNullOrEmpty(lsRemoteOutput))
            return null;

        // Format: "ref: refs/heads/<name>\tHEAD"
        foreach (var line in lsRemoteOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"^ref:\s+refs/heads/(.+?)\s+HEAD\b");
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }
        return null;
    }

    /// <summary>
    /// Runs a git command and captures stdout/stderr without risking a pipe-buffer deadlock:
    /// both streams are drained asynchronously while waiting for exit, rather than reading
    /// one to completion before starting the other.
    /// </summary>
    public static (int ExitCode, string StdOut, string StdErr) RunGit(string arguments, string workingDirectory, int timeoutMs = 60000)
    {
        var psi = MakeGitStartInfo(arguments, workingDirectory);
        using var process = Process.Start(psi)!;
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(true); } catch { /* best effort */ }
            var partialErr = errTask.IsCompletedSuccessfully ? errTask.Result : "";
            var timeoutMessage = $"git {arguments} timed out after {timeoutMs}ms";
            var combinedErr = string.IsNullOrEmpty(partialErr) ? timeoutMessage : $"{partialErr}\n{timeoutMessage}";
            return (-1, outTask.IsCompletedSuccessfully ? outTask.Result : "", combinedErr);
        }

        return (process.ExitCode, FileHelper.SanitizeUtf8(outTask.GetAwaiter().GetResult()), FileHelper.SanitizeUtf8(errTask.GetAwaiter().GetResult()));
    }

    internal static string? RunGitCapture(string? workingDir, string args, int timeoutMs)
    {
        try
        {
            var psi = MakeGitStartInfo(args, workingDir);

            using var process = Process.Start(psi);
            if (process == null) return null;

            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(true); } catch { /* best effort */ }
                return null;
            }

            var output = outTask.GetAwaiter().GetResult();
            _ = errTask.GetAwaiter().GetResult();

            return process.ExitCode == 0 ? FileHelper.SanitizeUtf8(output) : null;
        }
        catch
        {
            return null;
        }
    }

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
                var output = RunGitCapture(expandedPath, "show-ref", 5000);
                if (output != null)
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

                return false;
            });
        }
        return await Task.Run(() =>
        {
            var output = RunGitCapture(null, $"ls-remote --heads \"{expandedPath}\" \"{branchName}\"", 10000);
            if (output != null)
            {
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains($"refs/heads/{branchName}"))
                        return true;
                }
            }

            return false;
        });
    }

    private static bool RunGitShowRef(string repoPath, string refName)
    {
        return RunGitCapture(repoPath, $"show-ref --verify \"{refName}\"", 5000) != null;
    }

    /// <summary>
    /// Enumerates all worktree directories under a plan's Worktrees directory, supporting both flat
    /// (Worktrees/repo) and nested (Worktrees/owner/repo) directory structures.
    /// </summary>
    public static IEnumerable<string> EnumerateWorktreeDirectories(string worktreesDir, bool includeOrphans = false)
    {
        if (!Directory.Exists(worktreesDir)) yield break;

        var topDirs = Directory.GetDirectories(worktreesDir);
        var hasAnyGitFile = topDirs.Any(d => File.Exists(Path.Combine(d, ".git")) ||
                                             Directory.GetDirectories(d).Any(sd => File.Exists(Path.Combine(sd, ".git"))));

        foreach (var dir in topDirs)
        {
            if (File.Exists(Path.Combine(dir, ".git")))
            {
                yield return dir;
            }
            else
            {
                var subDirs = Directory.GetDirectories(dir);
                if (subDirs.Length > 0)
                {
                    var foundChild = false;
                    foreach (var subDir in subDirs)
                    {
                        if (File.Exists(Path.Combine(subDir, ".git")) || (!hasAnyGitFile && !includeOrphans) || includeOrphans)
                        {
                            foundChild = true;
                            yield return subDir;
                        }
                    }
                    if (!foundChild && includeOrphans)
                    {
                        yield return dir;
                    }
                }
                else if (includeOrphans || !hasAnyGitFile)
                {
                    yield return dir;
                }
            }
        }
    }

    /// <summary>
    /// Derives the relative worktree path for a repo (e.g. "ivy-interactive/ivy-tendril" or "my-repo").
    /// Checks for standard {TENDRIL_HOME}/Projects/{project}/Repos/{owner}/{repo} layout first,
    /// falls back to git remote origin owner/repo, and finally falls back to the leaf directory name.
    /// </summary>
    public static string DeriveWorktreeRelativePath(string repoPath)
    {
        var normalized = repoPath.TrimEnd('/', '\\');
        var matchRepos = Regex.Match(normalized, @"[\\/]Repos[\\/](?<subPath>.+)$", RegexOptions.IgnoreCase);
        if (matchRepos.Success)
        {
            var subPath = matchRepos.Groups["subPath"].Value;
            if (!string.IsNullOrWhiteSpace(subPath))
            {
                return subPath;
            }
        }

        if (Directory.Exists(normalized))
        {
            try
            {
                var (exitCode, url, _) = RunGit("remote get-url origin", normalized, 5000);
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(url))
                {
                    var trimmedUrl = url.Trim();
                    var isRemoteUrl = trimmedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                      trimmedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                      trimmedUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
                                      trimmedUrl.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase);

                    if (isRemoteUrl)
                    {
                        var match = Regex.Match(trimmedUrl, @"[/:](?<owner>[^/]+)/(?<name>[^/]+?)(?:\.git)?$", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            var owner = match.Groups["owner"].Value;
                            var name = match.Groups["name"].Value;
                            return Path.Combine(owner, name);
                        }
                    }
                }
            }
            catch
            {
                // Best-effort remote lookup
            }
        }

        var lastSlash = normalized.LastIndexOfAny(['/', '\\']);
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }
}

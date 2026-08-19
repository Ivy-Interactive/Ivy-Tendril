using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ivy.Tendril.Services.Git;

/// <summary>
///     How a plan's branch is disposed of once its worktree has been removed. The branch is often the
///     only ref holding the commits execution produced, so who asked for the removal decides whether
///     destroying it is acceptable.
/// </summary>
internal enum BranchDeleteMode
{
    /// <summary>
    ///     <c>git branch -D</c>: deletes the branch whatever it holds. Used by explicit user actions
    ///     — Complete, Discard, Reset to Draft, deleting the plan or its ExecutePlan job — where the
    ///     user has asked for this work to go away and is present to notice.
    /// </summary>
    Force,

    /// <summary>
    ///     <c>git branch -d</c>: git refuses to delete a branch holding commits that are not merged
    ///     into its base or upstream, and those branches are kept and logged instead. Used by the
    ///     unattended reaper, which runs on a timer with nobody watching and so must never silently
    ///     orphan commits. The worktree directory is reclaimed either way — only the 41-byte ref
    ///     survives.
    /// </summary>
    PreserveUnpushed
}

public class WorktreeCleanupService : IStartable, IDisposable
{
    private static readonly Regex SafeTitleRegex = new(@"^\d{5}-(.+)", RegexOptions.Compiled);

    // Terminal states: the user is done with the plan, so its worktree is reclaimed promptly.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
        { nameof(PlanStatus.Completed), nameof(PlanStatus.Skipped), nameof(PlanStatus.Icebox) };

    // Non-terminal states holding disposable work: Failed = a broken execution attempt, Draft = an
    // unstarted or reverted one. Re-executing recreates whatever they hold, so their worktree is
    // reclaimed once the plan has been idle past the stale-reaper window.
    //
    // Review is deliberately NOT in this set. It means execution finished, produced commits, and a
    // human is the blocker — there is no equivalent fallback, and reaping runs `git branch -D` on the
    // only ref holding those commits. A Review plan's worktree and branch must survive until the
    // human acts, so it is never reaped automatically (ResolveGrace returns null for it). Explicit
    // user actions — Create PR, Discard, Reset to Draft, deleting the ExecutePlan job or the plan,
    // and `tendril plan cleanup --force` — call RemoveWorktrees directly and are unaffected.
    private static readonly HashSet<string> StaleReapStates = new(StringComparer.OrdinalIgnoreCase)
        { nameof(PlanStatus.Failed), nameof(PlanStatus.Draft) };

    private static readonly TimeSpan DefaultTerminalGrace = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultStaleReaperPeriod = TimeSpan.FromDays(7);
    private static readonly TimeSpan DefaultTimerInterval = TimeSpan.FromMinutes(30);

    private readonly string _plansDirectory;
    private readonly ILogger<WorktreeCleanupService> _logger;
    private readonly IWorktreeLifecycleLogger? _lifecycleLogger;
    private readonly TimeSpan _terminalGrace;
    private readonly TimeSpan _staleReaperPeriod;
    private readonly TimeSpan _timerInterval;
    private Timer? _timer;

    public WorktreeCleanupService(string plansDirectory, ILogger<WorktreeCleanupService> logger,
        IWorktreeLifecycleLogger? lifecycleLogger = null, TimeSpan? terminalGrace = null,
        TimeSpan? staleReaperPeriod = null, TimeSpan? timerInterval = null)
    {
        _plansDirectory = plansDirectory;
        _logger = logger;
        _lifecycleLogger = lifecycleLogger;
        _terminalGrace = terminalGrace ?? DefaultTerminalGrace;
        _staleReaperPeriod = staleReaperPeriod ?? DefaultStaleReaperPeriod;
        _timerInterval = timerInterval ?? DefaultTimerInterval;
    }

    public void Start()
    {
        _timer = new Timer(_ => RunCleanup(), null, TimeSpan.FromMinutes(5), _timerInterval);
    }

    /// <summary>
    ///     Resolves how long a plan in the given state must be idle before its worktree is reaped,
    ///     or <c>null</c> for states whose worktrees are never reaped automatically: the
    ///     active/transient ones (Creating/Executing/Updating/Blocked) and Review, which holds
    ///     finished, unpushed work while it waits on a human.
    /// </summary>
    internal static TimeSpan? ResolveGrace(string state, TimeSpan terminalGrace, TimeSpan staleReaperPeriod)
    {
        if (TerminalStates.Contains(state)) return terminalGrace;
        if (StaleReapStates.Contains(state)) return staleReaperPeriod;
        return null;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    internal void RunCleanup()
    {
        try
        {
            if (!Directory.Exists(_plansDirectory)) return;

            // Regular plan-level worktree cleanup
            foreach (var dir in Directory.GetDirectories(_plansDirectory))
            {
                try
                {
                    CleanupPlanWorktrees(dir, _logger, _lifecycleLogger, _terminalGrace, _staleReaperPeriod);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup worktrees for {PlanFolder}", Path.GetFileName(dir));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Worktree cleanup scan failed");
        }
    }

    internal static void CleanupPlanWorktrees(string planFolderPath, ILogger? logger = null, IWorktreeLifecycleLogger? lifecycleLogger = null,
        TimeSpan? terminalGrace = null, TimeSpan? staleReaperPeriod = null)
    {
        var worktreesDir = Path.Combine(planFolderPath, "Worktrees");
        if (!Directory.Exists(worktreesDir)) return;

        var planYamlPath = Path.Combine(planFolderPath, "plan.yaml");
        if (!File.Exists(planYamlPath)) return;

        PlanYaml? planYaml;
        try
        {
            var yaml = FileHelper.ReadAllText(planYamlPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            planYaml = deserializer.Deserialize<PlanYaml>(yaml);
        }
        catch
        {
            return;
        }

        if (planYaml == null) return;

        var grace = ResolveGrace(planYaml.State, terminalGrace ?? DefaultTerminalGrace, staleReaperPeriod ?? DefaultStaleReaperPeriod);
        if (grace is null) return;

        if (DateTime.UtcNow - planYaml.Updated < grace.Value) return;

        var planId = WorktreeLifecycleLogger.ExtractPlanId(planFolderPath);

        logger?.LogInformation("Cleaning up worktrees for plan {PlanFolder} (state: {State}, updated: {Updated})",
            Path.GetFileName(planFolderPath), planYaml.State, planYaml.Updated.ToString("o", CultureInfo.InvariantCulture));

        // The reaper runs unattended on a timer, so it never force-deletes a branch: a Failed or
        // Draft plan can hold good commits its verifications never got to, and nothing would warn
        // before they became unreachable.
        RemoveWorktrees(planFolderPath, logger, lifecycleLogger, BranchDeleteMode.PreserveUnpushed);

        // Safety net: RemoveWorktrees should have removed all directories
        foreach (var wtDir in GitHelper.EnumerateWorktreeDirectories(worktreesDir))
        {
            logger?.LogWarning(
                "Worktree directory still exists after RemoveWorktrees (this should not happen): {Path}",
                Path.GetFileName(wtDir));

            lifecycleLogger?.LogCleanupAttempt(planId, wtDir, "CleanupPlanWorktrees(fallback)", gitFileExists: false);

            try
            {
                ForceDeleteDirectory(wtDir, logger);
                lifecycleLogger?.LogCleanupSuccess(planId, wtDir);
            }
            catch (Exception ex)
            {
                lifecycleLogger?.LogCleanupFailed(planId, wtDir, ex.Message);
                logger?.LogWarning(ex, "Failed to force-delete worktree directory {Dir}", Path.GetFileName(wtDir));
            }
        }

        // Remove the worktrees directory itself
        try
        {
            if (Directory.Exists(worktreesDir))
                ForceDeleteDirectory(worktreesDir, logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to force-delete worktrees directory {Dir}", worktreesDir);
        }
    }

    /// <summary>
    ///     Recursively deletes a directory with retry logic, clearing read-only attributes
    ///     and attempting to release file locks by shutting down build servers and killing
    ///     VBCSCompiler processes when <see cref="Directory.Delete(string, bool)"/> fails with
    ///     <see cref="UnauthorizedAccessException"/> or <see cref="IOException"/>.
    /// </summary>
    /// <remarks>
    ///     Windows <c>Directory.Delete</c> can fail on deeply nested paths (such as
    ///     <c>node_modules</c>) due to long-path limits, transient file locks, or
    ///     NTFS permission quirks. This method retries with exponential backoff and
    ///     applies mitigations (clear read-only attributes, shutdown build servers,
    ///     kill locking processes) before throwing.
    /// </remarks>
    /// <summary>
    ///     Removes a plan's execution work product: the Artifacts and Verification directories plus all
    ///     git worktrees. Used when resetting a plan to a clean Draft (Reset to Draft, or deleting an
    ///     ExecutePlan job). Job logs are NOT touched — they live in <c>&lt;TendrilHome&gt;/Jobs/</c> and
    ///     are the forensic record of runs that happened, which a reset must not erase.
    /// </summary>
    public static void CleanPlanState(string planFolderPath, ILogger? logger = null)
    {
        var artifactsDir = Path.Combine(planFolderPath, "Artifacts");
        if (Directory.Exists(artifactsDir))
        {
            logger?.LogInformation("Cleaning artifacts directory: {Path}", artifactsDir);
            ForceDeleteDirectory(artifactsDir, logger);
        }

        // Legacy: plans written before job logs moved to <TendrilHome>/Jobs/ may still carry a Logs/
        // folder. Nothing writes it any more; sweep it away when the plan is reset.
        var logsDir = Path.Combine(planFolderPath, "Logs");
        if (Directory.Exists(logsDir))
        {
            logger?.LogInformation("Cleaning legacy plan logs directory: {Path}", logsDir);
            ForceDeleteDirectory(logsDir, logger);
        }

        var verificationDir = Path.Combine(planFolderPath, "Verification");
        if (Directory.Exists(verificationDir))
        {
            logger?.LogInformation("Cleaning verification directory: {Path}", verificationDir);
            ForceDeleteDirectory(verificationDir, logger);
        }

        RemoveWorktrees(planFolderPath, logger);

        var worktreesDir = Path.Combine(planFolderPath, "Worktrees");
        if (Directory.Exists(worktreesDir))
        {
            logger?.LogInformation("Cleaning worktrees directory: {Path}", worktreesDir);
            ForceDeleteDirectory(worktreesDir, logger);
        }
    }

    internal static void ForceDeleteDirectory(string path, ILogger? logger = null)
    {
        const int maxRetries = 3;
        int[] delaysMs = [500, 1000, 1500];
        bool buildServersShutdown = false;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                logger?.LogDebug("ForceDeleteDirectory retry {Attempt}/{Max} for {Dir}",
                    attempt, maxRetries, Path.GetFileName(path));
                Thread.Sleep(delaysMs[attempt - 1]);
            }

            ClearReadOnlyAttributes(path);
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!buildServersShutdown)
                    {
                        TryShutdownBuildServers(logger);
                        buildServersShutdown = true;
                    }

                    if (attempt == maxRetries - 1)
                        TryKillLockingProcesses(path, logger);
                }

                if (attempt < maxRetries)
                    continue;

                if (OperatingSystem.IsWindows())
                    TryLogHandleHolders(path, logger);

                throw new IOException($"Failed to delete '{Path.GetFileName(path)}' after {maxRetries} retries", ex);
            }
        }
    }

    private static void TryLogHandleHolders(string path, ILogger? logger)
    {
        if (logger == null || !OperatingSystem.IsWindows()) return;
        try
        {
            var psi = new ProcessStartInfo("handle.exe")
            {
                ArgumentList = { "-accepteula", "-nobanner", path },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            if (!string.IsNullOrWhiteSpace(output))
                logger.LogWarning("Processes holding handles on {Dir}:\n{Output}", Path.GetFileName(path), output);
        }
        catch
        {
            // handle.exe not installed or failed — silently skip
        }
    }

    private static void TryShutdownBuildServers(ILogger? logger)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            logger?.LogInformation("Shutting down .NET build servers to release file locks");
            var psi = new ProcessStartInfo(PathHelper.GetDotnetPath(), "build-server shutdown")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(15000);
        }
        catch
        {
            // dotnet not available or failed — continue with retry
        }
    }

    private static void TryKillLockingProcesses(string path, ILogger? logger)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var psi = new ProcessStartInfo("handle.exe")
            {
                ArgumentList = { "-accepteula", "-nobanner", "-p", "VBCSCompiler", path },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            if (string.IsNullOrWhiteSpace(output)) return;

            // Parse handle.exe output for PIDs: "VBCSCompiler.exe pid: 1234 ..."
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(output, @"pid:\s*(\d+)"))
            {
                if (!int.TryParse(match.Groups[1].Value, out var pid)) continue;
                try
                {
                    var target = Process.GetProcessById(pid);
                    if (!target.ProcessName.Equals("VBCSCompiler", StringComparison.OrdinalIgnoreCase)) continue;
                    logger?.LogInformation("Killing VBCSCompiler (PID {Pid}) holding lock on {Dir}", pid, Path.GetFileName(path));
                    target.Kill();
                    target.WaitForExit(5000);
                }
                catch
                {
                    // Process already exited or access denied — continue
                }
            }
        }
        catch
        {
            // handle.exe not installed or failed — continue
        }
    }

    /// <summary>
    ///     Fire-and-forget worktree removal for terminal-state UI actions (Complete / Discard) so
    ///     disk is reclaimed promptly without blocking the UI thread. The background stale reaper
    ///     remains the backstop if this fails.
    /// </summary>
    internal static void RemoveWorktreesInBackground(string planFolderPath, ILogger? logger = null)
    {
        Task.Run(() =>
        {
            try
            {
                RemoveWorktrees(planFolderPath, logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Background worktree cleanup failed for {PlanFolder}", Path.GetFileName(planFolderPath));
            }
        });
    }

    /// <summary>
    ///     Fire-and-forget permanent deletion of an entire plan folder (worktrees + all
    ///     contents) for terminal UI delete actions, so slow disk I/O never blocks the
    ///     plan-write pipeline.
    /// </summary>
    internal static void DeletePlanFolderInBackground(string planFolderPath, ILogger? logger = null,
        IWorktreeLifecycleLogger? lifecycleLogger = null)
    {
        Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(planFolderPath)) return;

                // Briefly acquire+release the per-folder cross-process lock before deleting, so
                // any plan.yaml write already in flight (or queued) for this folder finishes
                // first instead of racing the delete. We can't hold the lock for the whole
                // deletion below — the lock file itself lives inside the folder being deleted,
                // so Directory.Delete would fail trying to remove a file we still have open.
                try
                {
                    using (PlanFileLock.Acquire(planFolderPath)) { }
                }
                catch (TimeoutException ex)
                {
                    logger?.LogWarning(ex, "Timed out waiting for plan lock before deleting {PlanFolder}; proceeding anyway",
                        Path.GetFileName(planFolderPath));
                }

                RemoveWorktrees(planFolderPath, logger, lifecycleLogger);
                ForceDeleteDirectory(planFolderPath, logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Background plan-folder deletion failed for {PlanFolder}",
                    Path.GetFileName(planFolderPath));
            }
        });
    }

    /// <summary>
    ///     Removes every git worktree under a plan's <c>Worktrees</c> directory and disposes of the
    ///     plan's branch according to <paramref name="branchDeleteMode" />. Defaults to
    ///     <see cref="BranchDeleteMode.Force" /> so explicit user actions keep behaving as before;
    ///     the automatic reaper passes <see cref="BranchDeleteMode.PreserveUnpushed" />.
    /// </summary>
    internal static void RemoveWorktrees(string planFolderPath, ILogger? logger = null, IWorktreeLifecycleLogger? lifecycleLogger = null,
        BranchDeleteMode branchDeleteMode = BranchDeleteMode.Force)
    {
        var worktreesDir = Path.Combine(planFolderPath, "Worktrees");
        if (!Directory.Exists(worktreesDir)) return;

        var planId = WorktreeLifecycleLogger.ExtractPlanId(planFolderPath);

        var safeTitle = ExtractSafeTitle(planFolderPath);
        var branchName = $"tendril/{planId}-{safeTitle}";

        foreach (var wtDir in GitHelper.EnumerateWorktreeDirectories(worktreesDir, includeOrphans: true))
        {
            var gitFile = Path.Combine(wtDir, ".git");
            if (!File.Exists(gitFile))
            {
                var dirAge = DateTime.UtcNow - new DirectoryInfo(wtDir).CreationTimeUtc;
                logger?.LogInformation(
                    "Worktree directory has no .git file (created {Age} ago), force-deleting: {Path}",
                    dirAge, Path.GetFileName(wtDir));
                lifecycleLogger?.LogCleanupAttempt(planId, wtDir, "RemoveWorktrees(force)", gitFileExists: false);

                try
                {
                    ForceDeleteDirectory(wtDir, logger);
                    lifecycleLogger?.LogCleanupSuccess(planId, wtDir);
                }
                catch (Exception ex)
                {
                    lifecycleLogger?.LogCleanupFailed(planId, wtDir, ex.Message);
                    logger?.LogWarning(ex, "Failed to force-delete worktree directory {Dir}", Path.GetFileName(wtDir));
                }
                continue;
            }

            var gitContent = FileHelper.ReadAllText(gitFile).Trim();
            var match = Regex.Match(gitContent, @"gitdir:\s*(.+)");
            if (!match.Success) continue;

            var gitDir = match.Groups[1].Value.Trim();
            var repoGitDir = Path.GetFullPath(Path.Combine(gitDir, "..", ".."));
            var repoRoot = Path.GetDirectoryName(repoGitDir);
            if (repoRoot == null || !Directory.Exists(repoRoot)) continue;

            lifecycleLogger?.LogCleanupAttempt(planId, wtDir, "RemoveWorktrees", gitFileExists: true);

            try
            {
                try
                {
                    var stopDaemonPsi = GitHelper.MakeGitStartInfo("fsmonitor--daemon stop", wtDir);
                    using var stopDaemonProc = Process.Start(stopDaemonPsi);
                    stopDaemonProc?.WaitForExitOrKill(5000);
                }
                catch
                {
                    // Best-effort: daemon may not be running, or fsmonitor may be unavailable.
                }

                var psi = GitHelper.MakeGitStartInfo($"worktree remove --force \"{wtDir}\"", repoRoot);
                using var process = Process.Start(psi);
                process.WaitForExitOrKill(10000);
                lifecycleLogger?.LogCleanupSuccess(planId, wtDir);

                DeletePlanBranch(repoRoot, branchName, branchDeleteMode, Path.GetFileName(wtDir), logger);
            }
            catch (Exception ex)
            {
                lifecycleLogger?.LogCleanupFailed(planId, wtDir, ex.Message);
            }
        }
    }

    /// <summary>
    ///     Deletes a plan's branch once its worktree has been removed.
    /// </summary>
    /// <remarks>
    ///     Under <see cref="BranchDeleteMode.PreserveUnpushed" /> this prefers git's safe delete
    ///     (<c>branch -d</c>), which refuses when the branch still holds commits that are not merged
    ///     into its base or upstream. Safe delete also refuses a branch whose commits were pushed but
    ///     which has no upstream configured, so a refusal is followed by an explicit check for a
    ///     remote-tracking ref containing the tip: if one exists the commits outlive the branch and
    ///     <c>branch -D</c> is safe. Otherwise the branch is kept and logged — a 41-byte ref costs far
    ///     less than the commits it is the only ref for.
    /// </remarks>
    internal static void DeletePlanBranch(string repoRoot, string branchName, BranchDeleteMode mode,
        string? worktreeName = null, ILogger? logger = null)
    {
        try
        {
            if (mode == BranchDeleteMode.Force)
            {
                ForceDeleteBranch(repoRoot, branchName, worktreeName, logger);
                return;
            }

            var (safeExit, _, safeErr) = GitHelper.RunGit($"branch -d \"{branchName}\"", repoRoot, 10000);
            if (safeExit == 0)
            {
                logger?.LogInformation("Deleted branch {BranchName} for worktree {WorktreeDir} (fully merged)",
                    branchName, worktreeName);
                return;
            }

            // No such branch — already deleted by a previous pass, or never created. Nothing to keep.
            var (tipExit, tipOut, _) = GitHelper.RunGit($"rev-parse --verify --quiet \"refs/heads/{branchName}\"", repoRoot, 10000);
            var tip = tipOut.Trim();
            if (tipExit != 0 || string.IsNullOrEmpty(tip))
            {
                logger?.LogDebug("Branch {BranchName} does not exist in {RepoRoot}; nothing to delete", branchName, repoRoot);
                return;
            }

            var (remotesExit, remotes, _) = GitHelper.RunGit($"branch -r --contains {tip}", repoRoot, 10000);
            if (remotesExit == 0 && !string.IsNullOrWhiteSpace(remotes))
            {
                ForceDeleteBranch(repoRoot, branchName, worktreeName, logger);
                return;
            }

            logger?.LogWarning(
                "Kept branch {BranchName} ({Tip}) in {RepoRoot}: it holds commits that are on no remote and are not merged " +
                "into its base or upstream, so deleting it would leave them reachable from nothing and the next `git gc` " +
                "would destroy them. Its worktree was still reclaimed; push the branch, or delete it manually with " +
                "`git branch -D`, once the work is no longer needed. git said: {Reason}",
                branchName, Shorten(tip), repoRoot, safeErr.Trim());
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to delete branch {BranchName} for worktree {WorktreeDir}", branchName, worktreeName);
        }
    }

    private static void ForceDeleteBranch(string repoRoot, string branchName, string? worktreeName, ILogger? logger)
    {
        var (exitCode, _, stdErr) = GitHelper.RunGit($"branch -D \"{branchName}\"", repoRoot, 10000);
        if (exitCode == 0)
            logger?.LogInformation("Deleted branch {BranchName} for worktree {WorktreeDir}", branchName, worktreeName);
        else
            logger?.LogWarning("Failed to delete branch {BranchName} for worktree {WorktreeDir}: {Error}",
                branchName, worktreeName, stdErr.Trim());
    }

    private static string Shorten(string hash) => hash.Length > 7 ? hash[..7] : hash;

    internal static string ExtractSafeTitle(string planFolderPath)
    {
        if (string.IsNullOrEmpty(planFolderPath))
            return "Unknown";
        var folderName = PathHelper.GetFileNameCrossPlatform(planFolderPath);
        var match = SafeTitleRegex.Match(folderName);
        return match.Success ? match.Groups[1].Value : "Unknown";
    }

    internal static void ClearReadOnlyAttributes(string directoryPath)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }
        catch
        {
            // Best-effort
        }
    }
}

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

public class WorktreeCleanupService : IStartable, IDisposable
{
    private static readonly Regex SafeTitleRegex = new(@"^\d{5}-(.+)", RegexOptions.Compiled);

    // Terminal states: the user is done with the plan, so its worktree is reclaimed promptly.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
        { nameof(PlanStatus.Completed), nameof(PlanStatus.Skipped), nameof(PlanStatus.Icebox) };

    // Non-terminal states that keep their worktree for recovery/resume (Failed = verifications
    // failed; Draft/Review = a stopped or reverted execution). The worktree is reaped
    // only once the plan has been idle past the stale-reaper window. Re-executing recreates it.
    private static readonly HashSet<string> StaleReapStates = new(StringComparer.OrdinalIgnoreCase)
        { nameof(PlanStatus.Failed), nameof(PlanStatus.Draft), nameof(PlanStatus.Review) };

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
    ///     or <c>null</c> for active/transient states (Creating/Executing/Updating/Blocked) whose
    ///     worktrees are never reaped.
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

        RemoveWorktrees(planFolderPath, logger, lifecycleLogger);

        // Safety net: RemoveWorktrees should have removed all directories
        foreach (var wtDir in Directory.GetDirectories(worktreesDir))
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
    ///     Removes a plan's execution work product: the Artifacts, Logs and Verification
    ///     directories plus all git worktrees. Used when resetting a plan to a clean Draft
    ///     (Reset to Draft, or deleting an ExecutePlan job).
    /// </summary>
    public static void CleanPlanState(string planFolderPath, ILogger? logger = null)
    {
        var artifactsDir = Path.Combine(planFolderPath, "Artifacts");
        if (Directory.Exists(artifactsDir))
        {
            logger?.LogInformation("Cleaning artifacts directory: {Path}", artifactsDir);
            ForceDeleteDirectory(artifactsDir, logger);
        }

        var logsDir = Path.Combine(planFolderPath, "Logs");
        if (Directory.Exists(logsDir))
        {
            logger?.LogInformation("Cleaning logs directory: {Path}", logsDir);
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

    internal static void RemoveWorktrees(string planFolderPath, ILogger? logger = null, IWorktreeLifecycleLogger? lifecycleLogger = null)
    {
        var worktreesDir = Path.Combine(planFolderPath, "Worktrees");
        if (!Directory.Exists(worktreesDir)) return;

        var planId = WorktreeLifecycleLogger.ExtractPlanId(planFolderPath);

        var safeTitle = ExtractSafeTitle(planFolderPath);
        var branchName = $"tendril/{planId}-{safeTitle}";

        foreach (var wtDir in Directory.GetDirectories(worktreesDir))
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
                var psi = new ProcessStartInfo("git", $"worktree remove --force \"{wtDir}\"")
                {
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process.WaitForExitOrKill(10000);
                lifecycleLogger?.LogCleanupSuccess(planId, wtDir);

                try
                {
                    var branchPsi = new ProcessStartInfo("git", $"branch -D \"{branchName}\"")
                    {
                        WorkingDirectory = repoRoot,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var branchProcess = Process.Start(branchPsi);
                    branchProcess.WaitForExitOrKill(10000);
                    logger?.LogInformation("Deleted branch {BranchName} for worktree {WorktreeDir}", branchName, Path.GetFileName(wtDir));
                }
                catch (Exception branchEx)
                {
                    logger?.LogWarning(branchEx, "Failed to delete branch {BranchName} for worktree {WorktreeDir}", branchName, Path.GetFileName(wtDir));
                }
            }
            catch (Exception ex)
            {
                lifecycleLogger?.LogCleanupFailed(planId, wtDir, ex.Message);
            }
        }
    }

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

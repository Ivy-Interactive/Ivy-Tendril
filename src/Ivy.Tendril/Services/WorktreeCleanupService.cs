using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Ivy.Helpers;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ivy.Tendril.Services;

public class WorktreeCleanupService : IStartable, IDisposable
{
    private static readonly Regex SafeTitleRegex = new(@"^\d{5}-(.+)", RegexOptions.Compiled);
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
        { "Completed", "Failed", "Skipped", "Icebox" };

    private static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TimerInterval = TimeSpan.FromMinutes(30);

    private readonly string _plansDirectory;
    private readonly ILogger<WorktreeCleanupService> _logger;
    private readonly IWorktreeLifecycleLogger? _lifecycleLogger;
    private Timer? _timer;

    public WorktreeCleanupService(string plansDirectory, ILogger<WorktreeCleanupService> logger, IWorktreeLifecycleLogger? lifecycleLogger = null)
    {
        _plansDirectory = plansDirectory;
        _logger = logger;
        _lifecycleLogger = lifecycleLogger;
    }

    public void Start()
    {
        _timer = new Timer(_ => RunCleanup(), null, TimeSpan.FromMinutes(5), TimerInterval);
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
                    CleanupPlanWorktrees(dir, _logger, _lifecycleLogger);
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

    internal static void CleanupPlanWorktrees(string planFolderPath, ILogger? logger = null, IWorktreeLifecycleLogger? lifecycleLogger = null)
    {
        var worktreesDir = Path.Combine(planFolderPath, "worktrees");
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

        if (!TerminalStates.Contains(planYaml.State)) return;

        if (DateTime.UtcNow - planYaml.Updated < GracePeriod) return;

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
    ///     Recursively deletes a directory, falling back to <c>cmd /c rmdir /s /q</c> on
    ///     Windows when <see cref="Directory.Delete(string, bool)"/> fails with
    ///     <see cref="UnauthorizedAccessException"/> or <see cref="IOException"/>.
    /// </summary>
    /// <remarks>
    ///     Windows <c>Directory.Delete</c> can fail on deeply nested paths (such as
    ///     <c>node_modules</c>) due to long-path limits, transient file locks, or
    ///     NTFS permission quirks. <c>rmdir /s /q</c> handles these cases more robustly.
    /// </remarks>
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
                if (!OperatingSystem.IsWindows()) throw;

                if (!buildServersShutdown)
                {
                    TryShutdownBuildServers(logger);
                    buildServersShutdown = true;
                }

                logger?.LogInformation("Directory.Delete failed for {Dir}, falling back to rmdir /s /q",
                    Path.GetFileName(path));

                var psi = new ProcessStartInfo("cmd.exe", $"/c rmdir /s /q \"{path}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(30000);

                if (!Directory.Exists(path))
                    return;

                if (attempt == maxRetries - 1)
                    TryKillLockingProcesses(path, logger);

                if (attempt < maxRetries)
                    continue;

                TryLogHandleHolders(path, logger);
                throw new IOException($"rmdir /s /q also failed to delete '{Path.GetFileName(path)}' after {maxRetries} retries", ex);
            }
        }
    }

    private static void TryLogHandleHolders(string path, ILogger? logger)
    {
        if (logger == null || !OperatingSystem.IsWindows()) return;
        try
        {
            var psi = new ProcessStartInfo("handle.exe", $"-accepteula -nobanner \"{path}\"")
            {
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
            var psi = new ProcessStartInfo("dotnet", "build-server shutdown")
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
            var psi = new ProcessStartInfo("handle.exe", $"-accepteula -nobanner -p VBCSCompiler \"{path}\"")
            {
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

    internal static void RemoveWorktrees(string planFolderPath, ILogger? logger = null, IWorktreeLifecycleLogger? lifecycleLogger = null)
    {
        var worktreesDir = Path.Combine(planFolderPath, "worktrees");
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
        var folderName = Path.GetFileName(planFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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

    private void CleanupPlanWorktrees(string planFolderPath)
    {
        CleanupPlanWorktrees(planFolderPath, _logger, _lifecycleLogger);
    }
}

using Ivy.Tendril.Models;
using System.Diagnostics;
using Ivy.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using Ivy.Tendril.Commands.DoctorChecks;

namespace Ivy.Tendril.Commands;

public static class DoctorCommand
{
    private static readonly string[] RequiredSoftware = ["gh", "git"];
    private static readonly string[] OptionalSoftware = ["pandoc"];

    private static readonly Dictionary<string, string> VersionArgs = new()
    {
        ["gh"] = "--version",
        ["claude"] = "--version",
        ["codex"] = "--version",
        ["gemini"] = "--version",
        ["git"] = "--version",
        ["pwsh"] = "-Version",
        ["pandoc"] = "--version"
    };

    private static readonly Dictionary<string, string> HealthArgs = new()
    {
        ["gh"] = "auth status",
        ["claude"] = "-p \"ping\" --max-turns 1",
        ["codex"] = "login status"
    };

    public static int Handle(string[] args)
    {
        if (args.Length == 0 || args[0] != "doctor") return -1;

        if (args.Length > 1 && args[1] == "plans")
            return DoctorPlansInternal(args.Skip(2).ToArray());

        return RunAsync().GetAwaiter().GetResult();
    }

    public static async Task<int> RunAsync()
    {
        ConfigService? configService = null;
        try
        {
            configService = new ConfigService();
        }
        catch
        {
            // ConfigService will be null, which checks will handle
        }

        var checks = new IDoctorCheck[]
        {
            new EnvironmentCheck(),
            new SoftwareCheck(configService),
            new DatabaseCheck(),
            new AgentModelsCheck(configService)
        };

        var hasErrors = false;
        foreach (var check in checks)
        {
            var result = await check.RunAsync();
            PrintCheckResult(check.Name, result);
            if (result.HasErrors) hasErrors = true;
        }

        // Summary
        AnsiConsole.WriteLine();
        if (hasErrors)
        {
            AnsiConsole.MarkupLine("[red]Issues found. Fix the errors above and re-run `tendril doctor`.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]All checks passed.[/]");
        return 0;
    }

    private static void PrintCheckResult(string checkName, CheckResult result)
    {
        PrintHeader(checkName);

        foreach (var status in result.Statuses)
        {
            PrintStatus(status.Label, status.Value, status.Kind);
        }
    }


    private static void PrintHeader(string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]── {title} ──[/]");
    }

    private static void PrintStatus(string label, string value, DoctorChecks.StatusKind kind)
    {
        var (symbol, color) = kind switch
        {
            DoctorChecks.StatusKind.Ok => ("✓", "green"),
            DoctorChecks.StatusKind.Warn => ("!", "yellow"),
            DoctorChecks.StatusKind.Error => ("✗", "red"),
            _ => (" ", "grey")
        };
        AnsiConsole.MarkupLine($"[{color}]  {symbol} {label.PadRight(40)}{value}[/]");
    }

    // --- doctor plans subcommand ---

    internal record PlanHealthResult(
        string Id,
        string Title,
        string State,
        int Worktrees,
        string Health,
        bool IsHealthy,
        string? FolderPath = null
    );

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    public static int DoctorPlansInternal(string[] args)
    {
        var showAll = args.Contains("--all");
        var fix = args.Contains("--fix");
        var prune = args.Contains("--prune");
        var stateFilter = GetArgValue(args, "--state");
        var worktreesOnly = args.Contains("--worktrees");

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (string.IsNullOrEmpty(tendrilHome))
        {
            AnsiConsole.MarkupLine("[red]TENDRIL_HOME is not set.[/]");
            return 1;
        }

        var plansDir = Environment.GetEnvironmentVariable("TENDRIL_PLANS")?.Trim() is { Length: > 0 } plans
            ? plans
            : Path.Combine(tendrilHome, "Plans");
        if (!Directory.Exists(plansDir))
        {
            AnsiConsole.MarkupLine($"[red]Plans directory not found: {plansDir}[/]");
            return 1;
        }

        AnsiConsole.WriteLine($"Scanning plans in: {plansDir}");
        Console.WriteLine();

        var allResults = ScanPlans(plansDir);

        if (fix)
        {
            HandleFixMode(allResults);
            allResults = ScanPlans(plansDir);
        }

        if (prune)
        {
            HandlePruneMode(allResults, plansDir);
            allResults = ScanPlans(plansDir);
        }

        var results = FilterResults(allResults, showAll, stateFilter, worktreesOnly);

        PrintPlansTable(results);
        PrintPlansSummary(allResults);

        return allResults.Any(r => !r.IsHealthy) ? 1 : 0;
    }

    private static void HandleFixMode(List<PlanHealthResult> allResults)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Repairing unhealthy plans...[/]");
        AnsiConsole.WriteLine();

        var repairedCount = 0;
        var failedCount = 0;

        foreach (var result in allResults.Where(r => !r.IsHealthy))
        {
            if (result.FolderPath == null) continue;

            var repairResult = RepairPlan(result.FolderPath, result);
            if (repairResult.Success)
            {
                AnsiConsole.MarkupLine($"[green]  ✓ {result.Id}: {repairResult.Message}[/]");
                repairedCount++;
            }
            else if (repairResult.Message != null)
            {
                AnsiConsole.MarkupLine($"[red]  ✗ {result.Id}: {repairResult.Message}[/]");
                failedCount++;
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine($"Repair summary: {repairedCount} repaired, {failedCount} failed");
        AnsiConsole.WriteLine();
    }

    private static void HandlePruneMode(List<PlanHealthResult> allResults, string plansDir)
    {
        var pruneCandidates = FindPruneCandidates(plansDir, allResults);
        if (pruneCandidates.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]Found {pruneCandidates.Count} plan(s) that appear to be test/junk data:[/]");
            AnsiConsole.WriteLine();

            foreach (var (_, result, reason) in pruneCandidates)
            {
                AnsiConsole.MarkupLine($"[grey]  {result.Id}-{result.Title}  ({reason})[/]");
            }

            AnsiConsole.WriteLine();
            if (AnsiConsole.Confirm("Remove these plans?", false))
            {
                var removed = 0;
                foreach (var (dir, result, _) in pruneCandidates)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        AnsiConsole.MarkupLine($"[green]  ✓ Removed {result.Id}-{result.Title}[/]");
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]  ✗ Failed to remove {result.Id}-{result.Title}: {ex.Message}[/]");
                    }
                }

                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine($"Pruned {removed} plan(s).");
            }
            else
            {
                AnsiConsole.WriteLine("Prune cancelled.");
            }

            AnsiConsole.WriteLine();
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine("No prune candidates found.");
        }
    }

    private static IEnumerable<PlanHealthResult> FilterResults(
        List<PlanHealthResult> allResults, bool showAll, string? stateFilter, bool worktreesOnly)
    {
        if (showAll)
            return allResults;
        if (stateFilter != null)
            return allResults.Where(r => r.State.Equals(stateFilter, StringComparison.OrdinalIgnoreCase));
        if (worktreesOnly)
            return allResults.Where(r => r.Worktrees > 0);
        return allResults.Where(r => !r.IsHealthy || r.State.Equals("Failed", StringComparison.OrdinalIgnoreCase));
    }

    internal static List<PlanHealthResult> ScanPlans(string plansDir)
    {
        var results = new List<PlanHealthResult>();

        var planDirs = Directory.GetDirectories(plansDir)
            .Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^\d{5}-"))
            .OrderBy(d => Path.GetFileName(d))
            .ToList();

        foreach (var dir in planDirs)
        {
            var folderName = Path.GetFileName(dir);
            var match = System.Text.RegularExpressions.Regex.Match(folderName, @"^(\d{5})-(.+)$");
            var id = match.Success ? match.Groups[1].Value : folderName;
            var title = match.Success ? match.Groups[2].Value : "";

            var yamlPath = Path.Combine(dir, "plan.yaml");
            var (yamlHealthy, yamlError, state) = CheckYamlHealth(yamlPath);
            var worktreeCount = CountWorktrees(dir);
            var hasStaleWorktrees = HasStaleWorktrees(dir);

            var recsError = CheckRecommendationsHealth(dir);

            var healthIssues = new List<string>();
            if (!yamlHealthy)
                healthIssues.Add($"YAML:{yamlError}");
            if (recsError != null)
                healthIssues.Add($"Recs:{recsError}");
            if (hasStaleWorktrees)
                healthIssues.Add("StaleWorktree");

            var health = healthIssues.Count == 0 ? "OK" : string.Join(",", healthIssues);

            results.Add(new PlanHealthResult(id, title, state, worktreeCount, health, healthIssues.Count == 0, dir));
        }

        return results;
    }

    internal static (bool Healthy, string? Error, string State) CheckYamlHealth(string yamlPath)
    {
        if (!File.Exists(yamlPath))
            return (false, "Missing", "Unknown");

        try
        {
            var content = File.ReadAllText(yamlPath);
            if (string.IsNullOrWhiteSpace(content))
                return (false, "Empty", "Unknown");

            // Try strict deserialization first
            try
            {
                var plan = Helpers.YamlHelper.Deserializer.Deserialize<Models.PlanYaml>(content);
                if (plan == null)
                    return (false, "Null after parse", "Unknown");

                if (string.IsNullOrWhiteSpace(plan.State))
                    return (false, "Missing state", "Unknown");

                if (string.IsNullOrWhiteSpace(plan.Project))
                    return (false, "Missing project", plan.State);

                if (string.IsNullOrWhiteSpace(plan.Title))
                    return (false, "Missing title", plan.State);

                if (plan.Repos == null || plan.Repos.Count == 0)
                {
                    var isCompleted = plan.State.Equals("Completed", StringComparison.OrdinalIgnoreCase);
                    var hasPrsOrCommits = (plan.Prs?.Count > 0) || (plan.Commits?.Count > 0);
                    if (isCompleted && hasPrsOrCommits)
                        return (true, null, plan.State);
                    return (false, "No repos", plan.State);
                }

                return (true, null, plan.State);
            }
            catch
            {
                // Fall back to regex extraction for plans with minor schema issues
                // (e.g. priority nested under dependsOn)
                var state = ExtractYamlField(content, "state") ?? "Unknown";
                var project = ExtractYamlField(content, "project");
                var title = ExtractYamlField(content, "title");

                if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(title))
                    return (false, "Parse error (missing required fields)", state);

                return (false, "Malformed YAML (readable)", state);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Read error: {ex.Message}", "Unknown");
        }
    }

    private static string? ExtractYamlField(string content, string field)
    {
        var match = System.Text.RegularExpressions.Regex.Match(content, $@"^{field}:\s*(.+)$",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim().Trim('\'', '"') : null;
    }

    internal static int CountWorktrees(string planPath)
    {
        var worktreesPath = Path.Combine(planPath, "worktrees");
        if (!Directory.Exists(worktreesPath))
            return 0;

        return Directory.GetDirectories(worktreesPath).Length;
    }

    internal static bool HasStaleWorktrees(string planPath)
    {
        var worktreesPath = Path.Combine(planPath, "worktrees");
        if (!Directory.Exists(worktreesPath))
            return false;

        try
        {
            foreach (var wtDir in Directory.GetDirectories(worktreesPath))
            {
                var gitFile = Path.Combine(wtDir, ".git");
                if (!File.Exists(gitFile))
                    return true;
            }
        }
        catch
        {
            // Ignore access errors
        }

        return false;
    }

    internal static string? CheckRecommendationsHealth(string planPath)
    {
        var planYamlPath = Path.Combine(planPath, "plan.yaml");
        if (!File.Exists(planYamlPath))
            return null;

        try
        {
            var content = File.ReadAllText(planYamlPath);
            var plan = Helpers.YamlHelper.Deserializer.Deserialize<Models.PlanYaml>(content);
            // Recommendations are optional — only validate if present
            if (plan?.Recommendations != null && plan.Recommendations.Count > 0)
                return null;

            return null;
        }
        catch (Exception ex)
        {
            return $"Parse error in plan.yaml: {ex.Message}";
        }
    }

    internal static void PrintPlansTable(IEnumerable<PlanHealthResult> results)
    {
        const int idWidth = 5;
        const int planWidth = 33;
        const int stateWidth = 10;
        const int wtWidth = 10;

        AnsiConsole.WriteLine(
            $"{"Id".PadRight(idWidth)}  {"Plan".PadRight(planWidth)}  {"State".PadRight(stateWidth)}  {"Worktrees".PadRight(wtWidth)}  Health");
        AnsiConsole.WriteLine(
            $"{new string('-', idWidth)}  {new string('-', planWidth)}  {new string('-', stateWidth)}  {new string('-', wtWidth)}  ------");

        foreach (var r in results)
        {
            var truncatedTitle = r.Title.Length > planWidth
                ? r.Title[..(planWidth - 3)] + "..."
                : r.Title;

            var healthColor = r.IsHealthy ? "green" : "red";
            var healthText = r.IsHealthy ? "OK" : r.Health;
            AnsiConsole.MarkupLine($"{r.Id.PadRight(idWidth)}  {truncatedTitle.PadRight(planWidth)}  {r.State.PadRight(stateWidth)}  {r.Worktrees.ToString().PadRight(wtWidth)}  [{healthColor}]{healthText}[/]");
        }
    }

    internal static void PrintPlansSummary(List<PlanHealthResult> allResults)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Summary:");
        AnsiConsole.WriteLine($"  Total plans: {allResults.Count}");
        AnsiConsole.WriteLine($"  Healthy: {allResults.Count(r => r.IsHealthy)}");
        AnsiConsole.WriteLine($"  Unhealthy: {allResults.Count(r => !r.IsHealthy)}");
        AnsiConsole.WriteLine($"  With worktrees: {allResults.Count(r => r.Worktrees > 0)}");
    }

    internal record RepairResult(bool Success, string? Message);

    internal static RepairResult RepairPlan(string planPath, PlanHealthResult healthResult)
    {
        var repairs = new List<string>();

        try
        {
            if (healthResult.Health.Contains("YAML:"))
            {
                var yamlPath = Path.Combine(planPath, "plan.yaml");

                if (!File.Exists(yamlPath))
                {
                    var folderTitle = TitleFromFolderName(Path.GetFileName(planPath));
                    var scaffold = $"state: Draft\nproject: Auto\ntitle: {EscapeYamlString(folderTitle)}\nlevel: NiceToHave\nrepos: []\ncommits: []\nprs: []\ncreated: {DateTime.UtcNow:O}\nupdated: {DateTime.UtcNow:O}\nverifications: []\nrelatedPlans: []\ndependsOn: []\n";
                    File.WriteAllText(yamlPath, scaffold);
                    repairs.Add("created missing plan.yaml");
                }
                else
                {
                    var content = File.ReadAllText(yamlPath);
                    var repaired = PlanReaderService.RepairPlanYaml(content);

                    var folderTitle = TitleFromFolderName(Path.GetFileName(planPath));

                    repaired = RepairYamlFields(repaired, folderTitle);
                    var changed = repaired != content;

                    if (changed)
                    {
                        File.WriteAllText(yamlPath, repaired);
                        repairs.Add("repaired plan.yaml");
                    }
                }
            }

            if (healthResult.Health.Contains("StaleWorktree"))
            {
                var worktreesPath = Path.Combine(planPath, "worktrees");
                if (Directory.Exists(worktreesPath))
                {
                    foreach (var wtDir in Directory.GetDirectories(worktreesPath))
                    {
                        if (!File.Exists(Path.Combine(wtDir, ".git")))
                            Directory.Delete(wtDir, true);
                    }
                    repairs.Add("removed stale worktrees");
                }
            }

            if (repairs.Count == 0)
                return new RepairResult(false, null);

            return new RepairResult(true, string.Join(", ", repairs));
        }
        catch (Exception ex)
        {
            return new RepairResult(false, $"repair failed: {ex.Message}");
        }
    }

    internal static string TitleFromFolderName(string folderName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(folderName, @"^\d{5}-(.+)$");
        if (!match.Success) return folderName;
        var raw = match.Groups[1].Value;
        var spaced = System.Text.RegularExpressions.Regex.Replace(raw, "(?<=[a-z])(?=[A-Z])", " ");
        return spaced.Replace('-', ' ');
    }

    internal static string RepairYamlFields(string content, string folderTitle)
    {
        var lines = content.Split('\n').ToList();

        EnsureYamlField(lines, "state", "Draft");
        EnsureYamlField(lines, "project", "Auto");
        EnsureYamlField(lines, "title", EscapeYamlString(folderTitle));
        foreach (var field in new[] { "repos", "commits", "prs", "verifications", "relatedPlans", "dependsOn" })
            EnsureYamlListNotNull(lines, field);

        return string.Join('\n', lines);
    }

    private static void EnsureYamlField(List<string> lines, string field, string defaultValue)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = System.Text.RegularExpressions.Regex.Match(lines[i], $@"^{field}:\s*(.*)$");
            if (!match.Success) continue;
            var value = match.Groups[1].Value.Trim().Trim('\'', '"');
            if (string.IsNullOrWhiteSpace(value))
                lines[i] = $"{field}: {defaultValue}";
            return;
        }
        lines.Insert(0, $"{field}: {defaultValue}");
    }

    private static void EnsureYamlListNotNull(List<string> lines, string field)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = System.Text.RegularExpressions.Regex.Match(lines[i], $@"^{field}:\s*$");
            if (!match.Success) continue;
            var nextIndex = i + 1;
            var hasListItems = nextIndex < lines.Count &&
                               lines[nextIndex].TrimStart().StartsWith("- ");
            if (!hasListItems)
                lines[i] = $"{field}: []";
            return;
        }
    }

    private static string EscapeYamlString(string value)
    {
        if (NeedsYamlEscaping(value))
            return "'" + value.Replace("'", "''") + "'";
        return value;
    }

    private static bool NeedsYamlEscaping(string value) =>
        value.Contains(':') || value.Contains('#') || value.Contains('\'');

    internal static List<(string Dir, PlanHealthResult Result, string Reason)> FindPruneCandidates(
        string plansDir, List<PlanHealthResult> allResults)
    {
        var candidates = new List<(string Dir, PlanHealthResult Result, string Reason)>();

        foreach (var result in allResults.Where(r => !r.IsHealthy))
        {
            if (result.FolderPath == null) continue;

            var reason = GetPruneReason(result.FolderPath, result);
            if (reason != null)
                candidates.Add((result.FolderPath, result, reason));
        }

        return candidates;
    }

    internal static string? GetPruneReason(string planPath, PlanHealthResult healthResult)
    {
        var hasPrs = false;
        var hasCommits = false;
        var hasRevisions = false;

        var yamlPath = Path.Combine(planPath, "plan.yaml");
        if (File.Exists(yamlPath))
        {
            var content = File.ReadAllText(yamlPath);
            hasPrs = HasYamlListItems(content, "prs");
            hasCommits = HasYamlListItems(content, "commits");
        }

        var revisionsDir = Path.Combine(planPath, "revisions");
        if (Directory.Exists(revisionsDir))
            hasRevisions = Directory.GetFiles(revisionsDir, "*.md").Length > 0;

        if (HasSignificantWork(hasPrs, hasCommits, hasRevisions))
            return null;

        var reasons = new List<string>();

        if (healthResult.Health.Contains("YAML:Missing"))
            reasons.Add("no plan.yaml");

        if (!HasSignificantWork(hasPrs, hasCommits, hasRevisions))
            reasons.Add("no PRs/commits/revisions");

        return reasons.Count > 0 ? string.Join(", ", reasons) : null;
    }

    private static bool HasYamlListItems(string content, string fieldName) =>
        System.Text.RegularExpressions.Regex.IsMatch(content,
            $@"^{fieldName}:\s*\n\s*-\s+\S", System.Text.RegularExpressions.RegexOptions.Multiline);

    private static bool HasSignificantWork(bool hasPrs, bool hasCommits, bool hasRevisions) =>
        hasPrs || hasCommits || hasRevisions;

}

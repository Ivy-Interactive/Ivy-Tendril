using System.ComponentModel;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanListSettings : CommandSettings
{
    [CommandOption("--state")]
    [Description("Filter by state (Draft, Executing, Failed, Completed, etc.)")]
    public string? State { get; init; }

    [CommandOption("--project")]
    [Description("Filter by project name")]
    public string? Project { get; init; }

    [CommandOption("--level")]
    [Description("Filter by level (Critical, Bug, NiceToHave, Backlog, Icebox)")]
    public string? Level { get; init; }

    [CommandOption("--has-pr")]
    [Description("Only plans with PRs")]
    public bool HasPr { get; init; }

    [CommandOption("--has-worktree")]
    [Description("Only plans with worktrees")]
    public bool HasWorktree { get; init; }

    [CommandOption("--format")]
    [Description("Output format: table (default), ids, folders, json")]
    public string? Format { get; init; }

    [CommandOption("--limit")]
    [Description("Maximum number of results")]
    public int? Limit { get; init; }
}

public class PlanListCommand : Command<PlanListSettings>
{
    protected override int Execute(CommandContext context, PlanListSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var plansDirectory = Environment.GetEnvironmentVariable("TENDRIL_PLANS")?.Trim();
            if (string.IsNullOrEmpty(plansDirectory))
            {
                var home = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
                if (string.IsNullOrEmpty(home))
                {
                    Console.Error.WriteLine("Error: TENDRIL_HOME environment variable is not set");
                    return 1;
                }
                plansDirectory = Path.Combine(home, "Plans");
            }

            if (!Directory.Exists(plansDirectory))
            {
                Console.Error.WriteLine($"Error: Plans directory not found: {plansDirectory}");
                return 1;
            }

            var results = ScanPlans(plansDirectory, settings);

            if (settings.Limit.HasValue && settings.Limit.Value > 0)
                results = results.Take(settings.Limit.Value).ToList();

            var format = (settings.Format ?? "table").ToLower();
            switch (format)
            {
                case "ids":
                    foreach (var r in results) Console.WriteLine(r.Id);
                    break;
                case "folders":
                    foreach (var r in results) Console.WriteLine(r.FolderName);
                    break;
                case "json":
                    Console.WriteLine("[");
                    for (var i = 0; i < results.Count; i++)
                    {
                        var r = results[i];
                        var comma = i < results.Count - 1 ? "," : "";
                        Console.WriteLine($"  {{\"id\":\"{Escape(r.Id)}\",\"title\":\"{Escape(r.Title)}\",\"state\":\"{Escape(r.State)}\",\"project\":\"{Escape(r.Project)}\",\"level\":\"{Escape(r.Level)}\"}}{comma}");
                    }
                    Console.WriteLine("]");
                    break;
                default:
                    if (results.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[dim]No plans found.[/]");
                        return 0;
                    }
                    var table = new Spectre.Console.Table();
                    table.AddColumn("Id");
                    table.AddColumn("Title");
                    table.AddColumn("State");
                    table.AddColumn("Project");
                    table.AddColumn("Level");
                    foreach (var r in results)
                        table.AddRow(
                            r.Id.EscapeMarkup(),
                            Truncate(r.Title, 40).EscapeMarkup(),
                            r.State.EscapeMarkup(),
                            r.Project.EscapeMarkup(),
                            r.Level.EscapeMarkup());
                    AnsiConsole.Write(table);
                    break;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    internal static List<PlanListEntry> ScanPlans(string plansDirectory, PlanListSettings settings)
    {
        var results = new List<PlanListEntry>();

        foreach (var dir in Directory.GetDirectories(plansDirectory))
        {
            var folderName = Path.GetFileName(dir);
            var dashIndex = folderName.IndexOf('-');
            if (dashIndex <= 0) continue;
            if (!int.TryParse(folderName[..dashIndex], out _)) continue;

            var yamlPath = Path.Combine(dir, "plan.yaml");
            if (!File.Exists(yamlPath)) continue;

            string state = "", project = "", title = "", level = "";
            bool hasPrs = false;

            try
            {
                var content = File.ReadAllText(yamlPath);
                state = ExtractField(content, "state");
                project = ExtractField(content, "project");
                title = ExtractField(content, "title");
                level = ExtractField(content, "level");
                hasPrs = content.Contains("- https://");
            }
            catch
            {
                continue;
            }

            // Apply filters
            if (!string.IsNullOrEmpty(settings.State) &&
                !state.Equals(settings.State, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(settings.Project) &&
                !project.Equals(settings.Project, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(settings.Level) &&
                !level.Equals(settings.Level, StringComparison.OrdinalIgnoreCase))
                continue;

            if (settings.HasPr && !hasPrs) continue;

            if (settings.HasWorktree)
            {
                var wtDir = Path.Combine(dir, "worktrees");
                if (!Directory.Exists(wtDir) || Directory.GetDirectories(wtDir).Length == 0)
                    continue;
            }

            results.Add(new PlanListEntry(
                folderName[..dashIndex],
                folderName,
                title,
                state,
                project,
                level));
        }

        return results;
    }

    private static string ExtractField(string content, string fieldName)
    {
        var prefix = fieldName + ":";
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                var value = trimmed[prefix.Length..].Trim();
                if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
                    value = value[1..^1];
                return value;
            }
        }
        return "";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    internal record PlanListEntry(string Id, string FolderName, string Title, string State, string Project, string Level);
}

using System.ComponentModel;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanListSettings : CommandSettings
{
    [CommandOption("--state")]
    [Description("Filter by state (Draft, Creating, Updating, Executing, Review, Failed, Completed, Skipped, Blocked, Icebox)")]
    public string? State { get; init; }

    [CommandOption("--project")]
    [Description("Filter by project name")]
    public string? Project { get; init; }

    [CommandOption("--level")]
    [Description("Filter by level (Bug, Feature, Epic, Chore, Nitpick)")]
    public string? Level { get; init; }

    [CommandOption("--has-pr")]
    [Description("Only plans with PRs")]
    public bool HasPr { get; init; }

    [CommandOption("--has-worktree")]
    [Description("Only plans with worktrees")]
    public bool HasWorktree { get; init; }

    [CommandOption("--search")]
    [Description("Filter by title or ID text")]
    public string? Search { get; init; }

    [CommandOption("--format")]
    [Description("Output format: table (default), ids, folders, json")]
    public string? Format { get; init; }

    [CommandOption("--limit")]
    [Description("Maximum number of results")]
    public int? Limit { get; init; }

    [CommandOption("--plans-dir")]
    [Description("Override plans directory path")]
    public string? PlansDir { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.ValidateOneOf(State, "--state", CliValidation.ValidStates),
            CliValidation.ValidateOneOf(Level, "--level", CliValidation.ValidLevels),
            CliValidation.ValidateOneOf(Format, "--format", CliValidation.ValidFormats),
            Limit.HasValue && Limit.Value <= 0
                ? Spectre.Console.ValidationResult.Error("--limit must be a positive integer.")
                : Spectre.Console.ValidationResult.Success()
        );
    }
}

public class PlanListCommand : Command<PlanListSettings>
{
    private readonly ConfigService _configService;

    public PlanListCommand(ConfigService configService)
    {
        _configService = configService;
    }

    protected override int Execute(CommandContext context, PlanListSettings settings, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(settings.Project))
        {
            var availableProjects = _configService.Projects.Select(p => p.Name).ToList();
            CliValidation.ValidateConfiguredProject(settings.Project, availableProjects);
        }

        string plansDirectory;
        if (!string.IsNullOrEmpty(settings.PlansDir))
        {
            plansDirectory = settings.PlansDir;
            if (!Directory.Exists(plansDirectory))
                throw new DirectoryNotFoundException($"Plans directory not found: {plansDirectory}");
        }
        else
        {
            plansDirectory = PlanCommandHelpers.GetPlansDirectory();
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
                var rows = results.Select(r => (IReadOnlyList<string>)new[]
                {
                    r.Id,
                    Truncate(r.Title, 40),
                    r.State,
                    r.Project,
                    r.Level
                });
                CliOutput.WriteTable(["Id", "Title", "State", "Project", "Level"], rows);
                break;
        }

        return 0;
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
                var wtDir = Path.Combine(dir, "Worktrees");
                if (!Directory.Exists(wtDir) || Directory.GetDirectories(wtDir).Length == 0)
                    continue;
            }

            if (!string.IsNullOrWhiteSpace(settings.Search))
            {
                var search = settings.Search.Trim().TrimStart('#').ToLowerInvariant();
                if (!title.ToLowerInvariant().Contains(search) &&
                    !folderName[..dashIndex].Contains(search))
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

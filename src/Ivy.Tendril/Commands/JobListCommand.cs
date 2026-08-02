using System.ComponentModel;
using Microsoft.Data.Sqlite;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class JobListSettings : CommandSettings
{
    [CommandOption("--project")]
    [Description("Filter by project name")]
    public string? Project { get; init; }

    [CommandOption("--status")]
    [Description("Filter by status (Pending, Queued, Running, Completed, Failed, Timeout, Stopped, Blocked)")]
    public string? Status { get; init; }

    [CommandOption("--type")]
    [Description("Filter by type (e.g., CreatePlan, ExecutePlan, CreatePr)")]
    public string? Type { get; init; }

    [CommandOption("--plan")]
    [Description("Filter by plan ID")]
    public string? Plan { get; init; }

    [CommandOption("--limit")]
    [Description("Maximum number of results (default: 50)")]
    public int? Limit { get; init; }

    [CommandOption("--format")]
    [Description("Output format: table (default), ids, json")]
    public string? Format { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.ValidateOneOf(Status, "--status", CliValidation.ValidJobStatuses),
            CliValidation.ValidateOneOf(Format, "--format", ["table", "ids", "json"]),
            Limit.HasValue && Limit.Value <= 0
                ? Spectre.Console.ValidationResult.Error("--limit must be a positive integer.")
                : Spectre.Console.ValidationResult.Success()
        );
    }
}

public class JobListCommand : Command<JobListSettings>
{
    private readonly ConfigService _configService;

    public JobListCommand(ConfigService configService)
    {
        _configService = configService;
    }

    protected override int Execute(CommandContext context, JobListSettings settings, CancellationToken cancellationToken)
    {
        var home = PathHelper.GetDefaultTendrilHome();
        var dbPath = Path.Combine(home, "tendril.db");

        if (!File.Exists(dbPath))
        {
            Console.WriteLine("No jobs found.");
            return 0;
        }

        if (!string.IsNullOrEmpty(settings.Project))
        {
            var availableProjects = _configService.Projects.Select(p => p.Name).ToList();
            if (!availableProjects.Contains(settings.Project, StringComparer.OrdinalIgnoreCase))
            {
                CliValidation.ThrowProjectNotFound(settings.Project, availableProjects);
            }
        }

        var jobs = QueryJobs(dbPath, settings);

        var format = (settings.Format ?? "table").ToLower();
        switch (format)
        {
            case "ids":
                foreach (var job in jobs)
                    Console.WriteLine(job.Id);
                break;
            case "json":
                Console.WriteLine("[");
                for (var i = 0; i < jobs.Count; i++)
                {
                    var j = jobs[i];
                    var comma = i < jobs.Count - 1 ? "," : "";
                    Console.WriteLine($"  {{\"id\":\"{Escape(j.Id)}\",\"type\":\"{Escape(j.Type)}\",\"project\":\"{Escape(j.Project)}\",\"status\":\"{Escape(j.Status)}\",\"plan\":\"{Escape(j.Plan)}\",\"started\":\"{Escape(j.Started)}\",\"duration\":\"{Escape(j.Duration)}\"}}{comma}");
                }
                Console.WriteLine("]");
                break;
            default:
                if (jobs.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No jobs found.[/]");
                    return 0;
                }
                var rows = jobs.Select(j => (IReadOnlyList<string>)new[]
                {
                    j.Id,
                    Truncate(j.Type, 20),
                    Truncate(j.Project, 20),
                    j.Status,
                    Truncate(j.Plan, 10),
                    j.Started,
                    j.Duration
                });
                CliOutput.WriteTable(["Id", "Type", "Project", "Status", "Plan", "Started", "Duration"], rows);
                break;
        }

        return 0;
    }

    internal static List<JobListEntry> QueryJobs(string dbPath, JobListSettings settings)
    {
        var jobs = new List<JobListEntry>();
        var limit = settings.Limit ?? 50;

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        var sql = "SELECT Id, Type, Project, Status, ReportedPlanId, StartedAt, CompletedAt, DurationSeconds, Cost FROM Jobs WHERE Cleared = 0";
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrEmpty(settings.Project))
        {
            sql += " AND Project = @project";
            parameters.Add(new SqliteParameter("@project", settings.Project));
        }

        if (!string.IsNullOrEmpty(settings.Status))
        {
            sql += " AND Status = @status";
            parameters.Add(new SqliteParameter("@status", settings.Status));
        }

        if (!string.IsNullOrEmpty(settings.Type))
        {
            sql += " AND Type = @type";
            parameters.Add(new SqliteParameter("@type", settings.Type));
        }

        if (!string.IsNullOrEmpty(settings.Plan))
        {
            sql += " AND ReportedPlanId = @plan";
            parameters.Add(new SqliteParameter("@plan", settings.Plan));
        }

        sql += " ORDER BY CASE WHEN CompletedAt IS NULL THEN 0 ELSE 1 END, StartedAt DESC LIMIT @limit";
        parameters.Add(new SqliteParameter("@limit", limit));

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var type = reader.GetString(1);
            var project = reader.GetString(2);
            var status = reader.GetString(3);
            var planId = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var startedAt = reader.IsDBNull(5) ? (DateTime?)null : DateTime.Parse(reader.GetString(5));
            var completedAt = reader.IsDBNull(6) ? (DateTime?)null : DateTime.Parse(reader.GetString(6));
            var durationSeconds = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);

            var started = startedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
            var duration = durationSeconds.HasValue ? FormatDuration(durationSeconds.Value) : "";

            jobs.Add(new JobListEntry(id, type, project, status, planId, started, duration));
        }

        return jobs;
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m";
        return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    internal record JobListEntry(string Id, string Type, string Project, string Status, string Plan, string Started, string Duration);
}

using Ivy.Tendril.Plugins;

namespace Ivy.Tendril.Plugins.Slack;

public static class SlackCommandHandler
{
    public const string HelpText =
        """
        *Tendril commands:*
        • `new <description>` — create a plan from a description
        • `new [project] <description>` — create a plan in a project (use `project:Name`)
        • `run <planId>` — execute a plan
        • `plans [state]` — list recent plans, optionally filtered by state
        • `projects` — list configured projects
        • `status <jobId>` — show job status
        • `help` — show this message
        """;

    public static string Execute(string text, ITendrilApi api)
    {
        try
        {
            return ExecuteCore(text, api);
        }
        catch (Exception ex)
        {
            return $":warning: {ex.Message}";
        }
    }

    private static string ExecuteCore(string text, ITendrilApi api)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
            return HelpText;

        var space = trimmed.IndexOf(' ');
        var command = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        var rest = space < 0 ? "" : trimmed[(space + 1)..].Trim();

        return command switch
        {
            "new" or "plan" or "create" => CreatePlan(rest, api),
            "run" or "execute" => ExecutePlan(rest, api),
            "plans" or "list" => ListPlans(rest, api),
            "projects" => ListProjects(api),
            "status" or "job" => JobStatus(rest, api),
            _ => HelpText
        };
    }

    private static string CreatePlan(string rest, ITendrilApi api)
    {
        if (rest.Length == 0)
            return "Usage: `new <description>` (optionally prefix with `project:Name`)";

        string? project = null;
        if (rest.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
        {
            var space = rest.IndexOf(' ');
            if (space < 0)
                return "Usage: `new project:Name <description>`";
            project = rest["project:".Length..space];
            rest = rest[(space + 1)..].Trim();
        }

        var jobId = api.StartCreatePlan(rest, project);
        return $":seedling: Plan creation started (job `{jobId}`{(project != null ? $", project *{project}*" : "")}). I'll post here when it finishes.";
    }

    private static string ExecutePlan(string rest, ITendrilApi api)
    {
        if (rest.Length == 0)
            return "Usage: `run <planId>`";
        var jobId = api.StartExecutePlan(rest);
        return $":rocket: Executing plan `{rest}` (job `{jobId}`). I'll post here when it finishes.";
    }

    private static string ListPlans(string rest, ITendrilApi api)
    {
        var state = rest.Length > 0 ? rest : null;
        var plans = api.ListPlans(state, limit: 15);
        if (plans.Count == 0)
            return state == null ? "No plans found." : $"No plans in state *{state}*.";

        var lines = plans.Select(p =>
            $"• `{p.Id}` *{p.Title ?? "(untitled)"}* — {p.State ?? "?"}{(p.Project != null ? $" · {p.Project}" : "")}");
        return string.Join("\n", lines);
    }

    private static string ListProjects(ITendrilApi api)
    {
        var projects = api.ListProjects();
        return projects.Count == 0
            ? "No projects configured."
            : "*Projects:*\n" + string.Join("\n", projects.Select(p => $"• {p}"));
    }

    private static string JobStatus(string rest, ITendrilApi api)
    {
        if (rest.Length == 0)
            return "Usage: `status <jobId>`";
        var job = api.GetJob(rest);
        if (job == null)
            return $"Job `{rest}` not found.";
        var message = string.IsNullOrEmpty(job.StatusMessage) ? "" : $"\n> {job.StatusMessage}";
        return $"*{job.Type}* `{job.Id}` — {job.Status}{(job.PlanFile != null ? $" · {job.PlanFile}" : "")}{message}";
    }
}

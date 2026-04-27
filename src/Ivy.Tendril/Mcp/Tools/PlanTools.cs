using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using ModelContextProtocol.Server;

namespace Ivy.Tendril.Mcp.Tools;

[McpServerToolType]
public sealed class PlanTools : AuthenticatedToolBase
{
    private readonly McpAuthenticationService _authService;

    public PlanTools(McpAuthenticationService authService) : base(authService)
    {
        _authService = authService;
    }

    private static readonly Regex FolderNameRegex = new(@"^(\d{5})-(.+)$", RegexOptions.Compiled);

    [McpServerTool(Name = "tendril_get_plan"), Description("Fetch plan metadata by ID or folder path")]
    public string GetPlan(
        [Description("Plan ID (e.g., '03228') or full folder path")] string planId,
        [Description("Optional field name to return a single value (state, project, level, title, repos, prs, commits, verifications, recommendations, etc.)")] string? field = null)
    {
        return ExecuteAuthenticated(() =>
        {
            try
            {
                var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
                var plan = PlanCommandHelpers.ReadPlan(planFolder);

                if (!string.IsNullOrEmpty(field))
                    return GetPlanField(plan, planFolder, field);

                return ReadPlanSummary(plan, planFolder);
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        });
    }

    [McpServerTool(Name = "tendril_list_plans"), Description("Query plans by state, project, or date range")]
    public string ListPlans(
        [Description("Filter by plan state (e.g., Draft, Executing, ReadyForReview, Failed, Completed)")] string? state = null,
        [Description("Filter by project name")] string? project = null,
        [Description("Filter plans created after this date (ISO 8601, e.g., 2026-04-01)")] string? since = null)
    {
        return ExecuteAuthenticated(() =>
        {
            try
            {
                var plansDir = PlanCommandHelpers.GetPlansDirectory();

                DateTime? sinceDate = null;
                if (!string.IsNullOrEmpty(since) && DateTime.TryParse(since, out var parsed))
                    sinceDate = parsed;

                var sb = new StringBuilder();
                var count = 0;

                foreach (var dir in Directory.GetDirectories(plansDir).OrderByDescending(d => Path.GetFileName(d)))
                {
                    var folderName = Path.GetFileName(dir);
                    var match = FolderNameRegex.Match(folderName);
                    if (!match.Success) continue;

                    PlanYaml yaml;
                    try { yaml = PlanCommandHelpers.ReadPlan(dir); }
                    catch { continue; }

                    if (!MatchesFilters(yaml, state, project, sinceDate))
                        continue;

                    var id = match.Groups[1].Value;
                    sb.AppendLine($"- [{id}] {yaml.Title} | State: {yaml.State} | Project: {yaml.Project} | Level: {yaml.Level}");
                    count++;

                    if (count >= 50)
                    {
                        sb.AppendLine("... (showing first 50 of potentially more results)");
                        break;
                    }
                }

                if (count == 0)
                    return "No plans found matching the specified criteria.";

                sb.Insert(0, $"Found {count} plan(s):\n");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        });
    }

    [McpServerTool(Name = "tendril_inbox"), Description("Create a new plan by writing to the Tendril inbox")]
    public string CreatePlan(
        [Description("Plan title/description")] string title,
        [Description("Project name (optional)")] string? project = null,
        [Description("Priority level: Critical, Important, NiceToHave (optional)")] string? level = null,
        [Description("Detailed prompt/description for plan creation (optional)")] string? prompt = null)
    {
        if (!_authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (string.IsNullOrEmpty(tendrilHome))
            return "Error: TENDRIL_HOME is not set.";

        var inboxDir = Path.Combine(tendrilHome, "Inbox");
        if (!Directory.Exists(inboxDir))
            Directory.CreateDirectory(inboxDir);

        var safeName = Regex.Replace(title, @"[^a-zA-Z0-9\s-]", "").Trim();
        safeName = Regex.Replace(safeName, @"\s+", "-");
        if (safeName.Length > 60) safeName = safeName[..60];
        var fileName = $"{safeName}-{DateTime.UtcNow:yyyyMMddHHmmss}.md";

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(project) || !string.IsNullOrEmpty(level))
        {
            sb.AppendLine("---");
            if (!string.IsNullOrEmpty(project))
                sb.AppendLine($"project: {project}");
            if (!string.IsNullOrEmpty(level))
                sb.AppendLine($"level: {level}");
            sb.AppendLine("---");
        }

        sb.AppendLine(title);
        if (!string.IsNullOrEmpty(prompt))
        {
            sb.AppendLine();
            sb.AppendLine(prompt);
        }

        var filePath = Path.Combine(inboxDir, fileName);
        File.WriteAllText(filePath, sb.ToString());

        return $"Plan submitted to inbox: {fileName}\nThe InboxWatcher will pick it up and create a plan automatically.";
    }

    [McpServerTool(Name = "tendril_plan_set"), Description("Set a scalar field on a plan")]
    public string SetField(
        [Description("Plan ID (e.g., '03228') or full folder path")] string planId,
        [Description("Field name (state, project, level, title, executionProfile, initialPrompt, sourceUrl, priority)")] string field,
        [Description("New value")] string value)
    {
        if (!_authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            switch (field.ToLower())
            {
                case "state": plan.State = value; break;
                case "project": plan.Project = value; break;
                case "level": plan.Level = value; break;
                case "title": plan.Title = value; break;
                case "executionprofile": plan.ExecutionProfile = value; break;
                case "initialprompt": plan.InitialPrompt = value; break;
                case "sourceurl": plan.SourceUrl = value; break;
                case "priority":
                    if (!int.TryParse(value, out var priority))
                        return $"Error: Invalid priority value: {value}. Must be an integer.";
                    plan.Priority = priority;
                    break;
                default:
                    return $"Error: Unknown field '{field}'. Valid: state, project, level, title, executionProfile, initialPrompt, sourceUrl, priority";
            }

            if (field.ToLower() != "updated")
                plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);
            return $"Updated {field} to '{value}'";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "tendril_plan_add_repo"), Description("Add a repository path to a plan")]
    public string AddRepo(
        [Description("Plan ID")] string planId,
        [Description("Repository path")] string repoPath)
    {
        return ModifyPlan(planId, plan =>
        {
            if (plan.Repos.Contains(repoPath, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Repository already in plan: {repoPath}");
            plan.Repos.Add(repoPath);
        }, $"Added repository: {repoPath}");
    }

    [McpServerTool(Name = "tendril_plan_remove_repo"), Description("Remove a repository path from a plan")]
    public string RemoveRepo(
        [Description("Plan ID")] string planId,
        [Description("Repository path")] string repoPath)
    {
        return ModifyPlan(planId, plan =>
        {
            var removed = plan.Repos.RemoveAll(r => r.Equals(repoPath, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                throw new InvalidOperationException($"Repository not found in plan: {repoPath}");
        }, $"Removed repository: {repoPath}");
    }

    [McpServerTool(Name = "tendril_plan_add_pr"), Description("Add a PR URL to a plan")]
    public string AddPr(
        [Description("Plan ID")] string planId,
        [Description("PR URL")] string prUrl)
    {
        return ModifyPlan(planId, plan =>
        {
            if (plan.Prs.Contains(prUrl))
                throw new InvalidOperationException($"PR already in plan: {prUrl}");
            plan.Prs.Add(prUrl);
        }, $"Added PR: {prUrl}");
    }

    [McpServerTool(Name = "tendril_plan_add_commit"), Description("Add a commit SHA to a plan")]
    public string AddCommit(
        [Description("Plan ID")] string planId,
        [Description("Commit SHA")] string sha)
    {
        return ModifyPlan(planId, plan =>
        {
            if (plan.Commits.Contains(sha))
                throw new InvalidOperationException($"Commit already in plan: {sha}");
            plan.Commits.Add(sha);
        }, $"Added commit: {sha}");
    }

    [McpServerTool(Name = "tendril_plan_set_verification"), Description("Set a verification status on a plan")]
    public string SetVerification(
        [Description("Plan ID")] string planId,
        [Description("Verification name (e.g., DotnetBuild, DotnetTest)")] string name,
        [Description("Status: Pending, Pass, Fail, Skipped")] string status)
    {
        if (!_authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var verification = plan.Verifications.FirstOrDefault(v =>
                v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (verification != null)
                verification.Status = status;
            else
                plan.Verifications.Add(new PlanVerificationEntry { Name = name, Status = status });

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return $"Set verification '{name}' to '{status}'";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "tendril_plan_add_log"), Description("Write an execution log entry to a plan")]
    public string AddLog(
        [Description("Plan ID")] string planId,
        [Description("Action name (e.g., CreatePlan, ExecutePlan)")] string action,
        [Description("Optional summary text")] string? summary = null)
    {
        if (!_authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var logPath = PlanAddLogCommand.WriteLog(planFolder, action, summary);
            return $"Log written: {Path.GetFileName(logPath)}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "tendril_plan_rec_add"), Description("Add a recommendation to a plan")]
    public string RecAdd(
        [Description("Plan ID")] string planId,
        [Description("Recommendation title")] string title,
        [Description("Recommendation description")] string description,
        [Description("Impact level: Small, Medium, High (optional)")] string? impact = null,
        [Description("Risk level: Small, Medium, High (optional)")] string? risk = null)
    {
        if (!_authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            plan.Recommendations ??= [];
            if (plan.Recommendations.Any(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                return $"Error: Recommendation '{title}' already exists";

            plan.Recommendations.Add(new RecommendationYaml
            {
                Title = title,
                Description = description,
                State = "Pending",
                Impact = impact,
                Risk = risk
            });

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return $"Added recommendation '{title}'";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "tendril_plan_rec_accept"), Description("Accept a recommendation")]
    public string RecAccept(
        [Description("Plan ID")] string planId,
        [Description("Recommendation title")] string title,
        [Description("Optional notes")] string? notes = null)
    {
        return ModifyPlan(planId, plan =>
        {
            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (rec == null)
                throw new InvalidOperationException($"Recommendation '{title}' not found");

            rec.State = string.IsNullOrEmpty(notes) ? "Accepted" : "AcceptedWithNotes";
            rec.DeclineReason = null;
        }, $"Accepted recommendation '{title}'");
    }

    [McpServerTool(Name = "tendril_plan_rec_decline"), Description("Decline a recommendation")]
    public string RecDecline(
        [Description("Plan ID")] string planId,
        [Description("Recommendation title")] string title,
        [Description("Decline reason (optional)")] string? reason = null)
    {
        return ModifyPlan(planId, plan =>
        {
            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (rec == null)
                throw new InvalidOperationException($"Recommendation '{title}' not found");

            rec.State = "Declined";
            rec.DeclineReason = reason;
        }, $"Declined recommendation '{title}'");
    }

    [McpServerTool(Name = "tendril_plan_rec_remove"), Description("Remove a recommendation from a plan")]
    public string RecRemove(
        [Description("Plan ID")] string planId,
        [Description("Recommendation title")] string title)
    {
        return ModifyPlan(planId, plan =>
        {
            var recs = plan.Recommendations ?? [];
            var match = recs.FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new InvalidOperationException($"Recommendation '{title}' not found");
            recs.Remove(match);
        }, $"Removed recommendation '{title}'");
    }

    [McpServerTool(Name = "tendril_plan_rec_list"), Description("List recommendations on a plan")]
    public string RecList(
        [Description("Plan ID")] string planId,
        [Description("Filter by state: Pending, Accepted, Declined (optional)")] string? state = null)
    {
        if (!_authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            var recs = plan.Recommendations ?? [];

            if (!string.IsNullOrEmpty(state))
                recs = recs.Where(r => r.State.Equals(state, StringComparison.OrdinalIgnoreCase)).ToList();

            if (recs.Count == 0)
                return "No recommendations found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {recs.Count} recommendation(s):");
            foreach (var rec in recs)
                sb.AppendLine($"- {rec.Title} | State: {rec.State} | Impact: {rec.Impact ?? "-"} | Risk: {rec.Risk ?? "-"}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private string ModifyPlan(string planId, Action<PlanYaml> modifier, string successMessage)
    {
        if (!_authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            modifier(plan);

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return successMessage;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static bool MatchesFilters(PlanYaml plan, string? state, string? project, DateTime? sinceDate)
    {
        if (!string.IsNullOrEmpty(state) && !string.Equals(plan.State, state, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(project) && !string.Equals(plan.Project, project, StringComparison.OrdinalIgnoreCase))
            return false;

        if (sinceDate.HasValue && plan.Created < sinceDate.Value)
            return false;

        return true;
    }

    private static readonly Dictionary<string, Func<PlanYaml, string, string>> FieldAccessors = new()
    {
        ["state"] = (p, _) => p.State,
        ["project"] = (p, _) => p.Project,
        ["level"] = (p, _) => p.Level,
        ["title"] = (p, _) => p.Title,
        ["created"] = (p, _) => p.Created.ToString("O"),
        ["updated"] = (p, _) => p.Updated.ToString("O"),
        ["executionprofile"] = (p, _) => p.ExecutionProfile ?? "",
        ["initialprompt"] = (p, _) => p.InitialPrompt ?? "",
        ["sourceurl"] = (p, _) => p.SourceUrl ?? "",
        ["priority"] = (p, _) => p.Priority.ToString(),
        ["repos"] = (p, _) => string.Join("\n", p.Repos),
        ["prs"] = (p, _) => string.Join("\n", p.Prs),
        ["commits"] = (p, _) => string.Join("\n", p.Commits),
        ["verifications"] = (p, _) => string.Join("\n", p.Verifications.Select(v => $"{v.Name}={v.Status}")),
        ["dependson"] = (p, _) => string.Join("\n", p.DependsOn),
        ["relatedplans"] = (p, _) => string.Join("\n", p.RelatedPlans),
        ["recommendations"] = (p, _) => string.Join("\n", (p.Recommendations ?? []).Select(r => $"{r.Title}={r.State}"))
    };

    private static string GetPlanField(PlanYaml plan, string planFolder, string field)
    {
        var key = field.ToLower();
        return FieldAccessors.TryGetValue(key, out var accessor)
            ? accessor(plan, planFolder)
            : $"Error: Unknown field '{field}'";
    }

    private static string ReadPlanSummary(PlanYaml plan, string planFolder)
    {
        var folderName = Path.GetFileName(planFolder);
        var match = FolderNameRegex.Match(folderName);
        var id = match.Success ? match.Groups[1].Value : folderName;

        var sb = new StringBuilder();
        sb.AppendLine($"# Plan {id}: {plan.Title}");
        sb.AppendLine();
        sb.AppendLine($"- **State:** {plan.State}");
        sb.AppendLine($"- **Project:** {plan.Project}");
        sb.AppendLine($"- **Level:** {plan.Level}");
        sb.AppendLine($"- **Created:** {plan.Created:O}");
        sb.AppendLine($"- **Updated:** {plan.Updated:O}");

        if (plan.Repos.Count > 0)
            sb.AppendLine($"- **Repos:** {string.Join(", ", plan.Repos)}");

        if (plan.Commits.Count > 0)
            sb.AppendLine($"- **Commits:** {string.Join(", ", plan.Commits)}");

        if (plan.Prs.Count > 0)
            sb.AppendLine($"- **PRs:** {string.Join(", ", plan.Prs)}");

        if (!string.IsNullOrEmpty(plan.InitialPrompt))
        {
            sb.AppendLine();
            sb.AppendLine("## Initial Prompt");
            sb.AppendLine(plan.InitialPrompt);
        }

        var revisionsDir = Path.Combine(planFolder, "revisions");
        if (Directory.Exists(revisionsDir))
        {
            var revFiles = Directory.GetFiles(revisionsDir, "*.md").OrderByDescending(f => f).ToArray();
            if (revFiles.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"## Latest Revision ({Path.GetFileName(revFiles[0])})");
                try
                {
                    var content = File.ReadAllText(revFiles[0]);
                    if (content.Length > 2000)
                        content = content[..2000] + "\n\n... (truncated)";
                    sb.AppendLine(content);
                }
                catch
                {
                    sb.AppendLine("(Could not read revision)");
                }
            }
        }

        return sb.ToString();
    }
}

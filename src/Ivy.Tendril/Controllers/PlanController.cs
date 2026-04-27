using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

internal static class PlanFieldAccessors
{
    private static readonly Dictionary<string, Func<PlanYaml, string?>> Getters = new()
    {
        ["state"] = p => p.State,
        ["project"] = p => p.Project,
        ["level"] = p => p.Level,
        ["title"] = p => p.Title,
        ["created"] = p => p.Created.ToString("O"),
        ["updated"] = p => p.Updated.ToString("O"),
        ["executionprofile"] = p => p.ExecutionProfile,
        ["initialprompt"] = p => p.InitialPrompt,
        ["sourceurl"] = p => p.SourceUrl,
        ["priority"] = p => p.Priority.ToString()
    };

    private static readonly Dictionary<string, Action<PlanYaml, string>> Setters = new()
    {
        ["state"] = (p, v) => p.State = v,
        ["project"] = (p, v) => p.Project = v,
        ["level"] = (p, v) => p.Level = v,
        ["title"] = (p, v) => p.Title = v,
        ["executionprofile"] = (p, v) => p.ExecutionProfile = v,
        ["initialprompt"] = (p, v) => p.InitialPrompt = v,
        ["sourceurl"] = (p, v) => p.SourceUrl = v,
        ["priority"] = (p, v) => p.Priority = int.Parse(v)
    };

    public static string? GetField(PlanYaml plan, string field) =>
        Getters.TryGetValue(field.ToLower(), out var getter)
            ? getter(plan)
            : null;

    public static bool TrySetField(PlanYaml plan, string field, string value, out string? error)
    {
        if (!Setters.TryGetValue(field.ToLower(), out var setter))
        {
            error = $"Unknown field: {field}";
            return false;
        }

        try
        {
            setter(plan, value);
            error = null;
            return true;
        }
        catch (FormatException)
        {
            error = $"Invalid value for {field}: {value}";
            return false;
        }
    }
}

[ApiController]
[Route("api/plans")]
public class PlanController : ControllerBase
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanController(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    private IActionResult ModifyPlanEndpoint(
        string planId,
        Func<PlanYaml, (bool success, string message, int statusCode)> modifier)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var (success, message, statusCode) = modifier(plan);

            if (success)
            {
                plan.Updated = DateTime.UtcNow;
                PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);
            }

            return statusCode switch
            {
                200 => Ok(new { message }),
                404 => NotFound(new { error = message }),
                409 => Conflict(new { error = message }),
                _ => StatusCode(statusCode, new { message })
            };
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new { error = $"Plan '{planId}' not found" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{planId}")]
    public IActionResult GetPlan(string planId, [FromQuery] string? field = null)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (!string.IsNullOrEmpty(field))
                return Ok(new { value = PlanFieldAccessors.GetField(plan, field) });

            return Ok(PlanToDto(plan, planFolder));
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new { error = $"Plan '{planId}' not found" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult ListPlans(
        [FromQuery] string? state = null,
        [FromQuery] string? project = null,
        [FromQuery] int limit = 50)
    {
        try
        {
            var plansDir = PlanCommandHelpers.GetPlansDirectory();
            var results = new List<object>();

            foreach (var dir in Directory.GetDirectories(plansDir).OrderByDescending(d => Path.GetFileName(d)))
            {
                var folderName = Path.GetFileName(dir);
                if (!ExtractPlanId(folderName, out var id)) continue;

                PlanYaml yaml;
                try { yaml = PlanCommandHelpers.ReadPlan(dir); }
                catch { continue; }

                if (!string.IsNullOrEmpty(state) &&
                    !string.Equals(yaml.State, state, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(project) &&
                    !string.Equals(yaml.Project, project, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new { id, title = yaml.Title, state = yaml.State, project = yaml.Project, level = yaml.Level });

                if (results.Count >= limit) break;
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{planId}")]
    public IActionResult SetField(string planId, [FromBody] SetFieldRequest request)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (!PlanFieldAccessors.TrySetField(plan, request.Field, request.Value, out var error))
                return BadRequest(new { error });

            if (request.Field.ToLower() != "updated")
                plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);
            return Ok(new { message = $"Updated {request.Field} to '{request.Value}'" });
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new { error = $"Plan '{planId}' not found" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{planId}/repos")]
    public IActionResult AddRepo(string planId, [FromBody] AddRepoRequest request) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            if (plan.Repos.Contains(request.RepoPath, StringComparer.OrdinalIgnoreCase))
                return (true, $"Repository already in plan: {request.RepoPath}", 200);

            plan.Repos.Add(request.RepoPath);
            return (true, $"Added repository: {request.RepoPath}", 200);
        });

    [HttpDelete("{planId}/repos")]
    public IActionResult RemoveRepo(string planId, [FromBody] RemoveRepoRequest request) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            var removed = plan.Repos.RemoveAll(r => r.Equals(request.RepoPath, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return (false, $"Repository not found in plan: {request.RepoPath}", 404);

            return (true, $"Removed repository: {request.RepoPath}", 200);
        });

    [HttpPost("{planId}/prs")]
    public IActionResult AddPr(string planId, [FromBody] AddPrRequest request) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            if (plan.Prs.Contains(request.PrUrl))
                return (true, $"PR already in plan: {request.PrUrl}", 200);

            plan.Prs.Add(request.PrUrl);
            return (true, $"Added PR: {request.PrUrl}", 200);
        });

    [HttpPost("{planId}/commits")]
    public IActionResult AddCommit(string planId, [FromBody] AddCommitRequest request) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            if (plan.Commits.Contains(request.Sha))
                return (true, $"Commit already in plan: {request.Sha}", 200);

            plan.Commits.Add(request.Sha);
            return (true, $"Added commit: {request.Sha}", 200);
        });

    [HttpPut("{planId}/verifications")]
    public IActionResult SetVerification(string planId, [FromBody] SetVerificationRequest request) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            var verification = plan.Verifications.FirstOrDefault(v =>
                v.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));

            if (verification != null)
                verification.Status = request.Status;
            else
                plan.Verifications.Add(new PlanVerificationEntry { Name = request.Name, Status = request.Status });

            return (true, $"Set verification '{request.Name}' to '{request.Status}'", 200);
        });

    [HttpPost("{planId}/logs")]
    public IActionResult AddLog(string planId, [FromBody] AddLogRequest request)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var logPath = PlanAddLogCommand.WriteLog(planFolder, request.Action, request.Summary);
            return Ok(new { message = $"Log written: {Path.GetFileName(logPath)}" });
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new { error = $"Plan '{planId}' not found" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{planId}/recommendations")]
    public IActionResult ListRecommendations(string planId, [FromQuery] string? state = null)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            var recs = plan.Recommendations ?? [];

            if (!string.IsNullOrEmpty(state))
                recs = recs.Where(r => r.State.Equals(state, StringComparison.OrdinalIgnoreCase)).ToList();

            return Ok(recs.Select(r => new
            {
                title = r.Title,
                description = r.Description,
                state = r.State,
                impact = r.Impact,
                risk = r.Risk,
                declineReason = r.DeclineReason
            }));
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new { error = $"Plan '{planId}' not found" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{planId}/recommendations")]
    public IActionResult AddRecommendation(string planId, [FromBody] AddRecRequest request) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            plan.Recommendations ??= [];
            if (plan.Recommendations.Any(r => r.Title.Equals(request.Title, StringComparison.OrdinalIgnoreCase)))
                return (false, $"Recommendation '{request.Title}' already exists", 409);

            plan.Recommendations.Add(new RecommendationYaml
            {
                Title = request.Title,
                Description = request.Description ?? "",
                State = "Pending",
                Impact = request.Impact,
                Risk = request.Risk
            });

            return (true, $"Added recommendation '{request.Title}'", 200);
        });

    [HttpPut("{planId}/recommendations/{title}/accept")]
    public IActionResult AcceptRecommendation(string planId, string title, [FromBody] AcceptRecRequest? request = null) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (rec == null)
                return (false, $"Recommendation '{title}' not found", 404);

            rec.State = string.IsNullOrEmpty(request?.Notes) ? "Accepted" : "AcceptedWithNotes";
            rec.DeclineReason = null;

            return (true, $"Accepted recommendation '{title}'", 200);
        });

    [HttpPut("{planId}/recommendations/{title}/decline")]
    public IActionResult DeclineRecommendation(string planId, string title, [FromBody] DeclineRecRequest? request = null) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (rec == null)
                return (false, $"Recommendation '{title}' not found", 404);

            rec.State = "Declined";
            rec.DeclineReason = request?.Reason;

            return (true, $"Declined recommendation '{title}'", 200);
        });

    [HttpDelete("{planId}/recommendations/{title}")]
    public IActionResult RemoveRecommendation(string planId, string title) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            var recs = plan.Recommendations ?? [];
            var match = recs.FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return (false, $"Recommendation '{title}' not found", 404);

            recs.Remove(match);
            return (true, $"Removed recommendation '{title}'", 200);
        });

    private static bool ExtractPlanId(string folderName, out string id)
    {
        id = "";
        var dash = folderName.IndexOf('-');
        if (dash <= 0) return false;
        var prefix = folderName[..dash];
        if (!int.TryParse(prefix, out _)) return false;
        id = prefix;
        return true;
    }

    private static object PlanToDto(PlanYaml plan, string planFolder)
    {
        var folderName = Path.GetFileName(planFolder);
        var dash = folderName.IndexOf('-');
        var id = dash > 0 ? folderName[..dash] : folderName;

        return new
        {
            id,
            title = plan.Title,
            state = plan.State,
            project = plan.Project,
            level = plan.Level,
            created = plan.Created,
            updated = plan.Updated,
            repos = plan.Repos,
            prs = plan.Prs,
            commits = plan.Commits,
            verifications = plan.Verifications.Select(v => new { name = v.Name, status = v.Status }),
            relatedPlans = plan.RelatedPlans,
            dependsOn = plan.DependsOn,
            priority = plan.Priority,
            executionProfile = plan.ExecutionProfile,
            initialPrompt = plan.InitialPrompt,
            sourceUrl = plan.SourceUrl,
            recommendations = (plan.Recommendations ?? []).Select(r => new
            {
                title = r.Title,
                description = r.Description,
                state = r.State,
                impact = r.Impact,
                risk = r.Risk
            })
        };
    }
}

// Request DTOs
public record SetFieldRequest(string Field, string Value);
public record AddRepoRequest(string RepoPath);
public record RemoveRepoRequest(string RepoPath);
public record AddPrRequest(string PrUrl);
public record AddCommitRequest(string Sha);
public record SetVerificationRequest(string Name, string Status);
public record AddLogRequest(string Action, string? Summary = null);
public record AddRecRequest(string Title, string? Description = null, string? Impact = null, string? Risk = null);
public record AcceptRecRequest(string? Notes = null);
public record DeclineRecRequest(string? Reason = null);

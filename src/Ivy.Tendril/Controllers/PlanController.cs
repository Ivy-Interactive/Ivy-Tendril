using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ivy.Tendril.Controllers;

[ApiController]
[Route("api/plans")]
public class PlanController : ControllerBase
{
    [HttpGet("{planId}")]
    public IActionResult GetPlan(string planId, [FromQuery] string? field = null)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (!string.IsNullOrEmpty(field))
                return Ok(new { value = GetField(plan, planFolder, field) });

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

            switch (request.Field.ToLower())
            {
                case "state": plan.State = request.Value; break;
                case "project": plan.Project = request.Value; break;
                case "level": plan.Level = request.Value; break;
                case "title": plan.Title = request.Value; break;
                case "executionprofile": plan.ExecutionProfile = request.Value; break;
                case "initialprompt": plan.InitialPrompt = request.Value; break;
                case "sourceurl": plan.SourceUrl = request.Value; break;
                case "priority":
                    if (!int.TryParse(request.Value, out var priority))
                        return BadRequest(new { error = $"Invalid priority: {request.Value}" });
                    plan.Priority = priority;
                    break;
                default:
                    return BadRequest(new { error = $"Unknown field: {request.Field}" });
            }

            if (request.Field.ToLower() != "updated")
                plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);
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
    public IActionResult AddRepo(string planId, [FromBody] AddRepoRequest request)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (plan.Repos.Contains(request.RepoPath, StringComparer.OrdinalIgnoreCase))
                return Ok(new { message = $"Repository already in plan: {request.RepoPath}" });

            plan.Repos.Add(request.RepoPath);
            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return Ok(new { message = $"Added repository: {request.RepoPath}" });
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

    [HttpDelete("{planId}/repos")]
    public IActionResult RemoveRepo(string planId, [FromBody] RemoveRepoRequest request)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var removed = plan.Repos.RemoveAll(r => r.Equals(request.RepoPath, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return NotFound(new { error = $"Repository not found in plan: {request.RepoPath}" });

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return Ok(new { message = $"Removed repository: {request.RepoPath}" });
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

    [HttpPost("{planId}/prs")]
    public IActionResult AddPr(string planId, [FromBody] AddPrRequest request)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (plan.Prs.Contains(request.PrUrl))
                return Ok(new { message = $"PR already in plan: {request.PrUrl}" });

            plan.Prs.Add(request.PrUrl);
            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return Ok(new { message = $"Added PR: {request.PrUrl}" });
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

    [HttpPost("{planId}/commits")]
    public IActionResult AddCommit(string planId, [FromBody] AddCommitRequest request)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (plan.Commits.Contains(request.Sha))
                return Ok(new { message = $"Commit already in plan: {request.Sha}" });

            plan.Commits.Add(request.Sha);
            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return Ok(new { message = $"Added commit: {request.Sha}" });
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

    [HttpPut("{planId}/verifications")]
    public IActionResult SetVerification(string planId, [FromBody] SetVerificationRequest request)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var verification = plan.Verifications.FirstOrDefault(v =>
                v.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));

            if (verification != null)
                verification.Status = request.Status;
            else
                plan.Verifications.Add(new PlanVerificationEntry { Name = request.Name, Status = request.Status });

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return Ok(new { message = $"Set verification '{request.Name}' to '{request.Status}'" });
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
                title = r.Title, description = r.Description, state = r.State,
                impact = r.Impact, risk = r.Risk, declineReason = r.DeclineReason
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
    public IActionResult AddRecommendation(string planId, [FromBody] AddRecRequest request)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            plan.Recommendations ??= [];
            if (plan.Recommendations.Any(r => r.Title.Equals(request.Title, StringComparison.OrdinalIgnoreCase)))
                return Conflict(new { error = $"Recommendation '{request.Title}' already exists" });

            plan.Recommendations.Add(new RecommendationYaml
            {
                Title = request.Title,
                Description = request.Description ?? "",
                State = "Pending",
                Impact = request.Impact,
                Risk = request.Risk
            });

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return Ok(new { message = $"Added recommendation '{request.Title}'" });
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

    [HttpPut("{planId}/recommendations/{title}/accept")]
    public IActionResult AcceptRecommendation(string planId, string title, [FromBody] AcceptRecRequest? request = null)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (rec == null)
                return NotFound(new { error = $"Recommendation '{title}' not found" });

            rec.State = string.IsNullOrEmpty(request?.Notes) ? "Accepted" : "AcceptedWithNotes";
            rec.DeclineReason = null;

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return Ok(new { message = $"Accepted recommendation '{title}'" });
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

    [HttpPut("{planId}/recommendations/{title}/decline")]
    public IActionResult DeclineRecommendation(string planId, string title, [FromBody] DeclineRecRequest? request = null)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (rec == null)
                return NotFound(new { error = $"Recommendation '{title}' not found" });

            rec.State = "Declined";
            rec.DeclineReason = request?.Reason;

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return Ok(new { message = $"Declined recommendation '{title}'" });
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

    [HttpDelete("{planId}/recommendations/{title}")]
    public IActionResult RemoveRecommendation(string planId, string title)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var recs = plan.Recommendations ?? [];
            var match = recs.FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return NotFound(new { error = $"Recommendation '{title}' not found" });

            recs.Remove(match);
            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);
            return Ok(new { message = $"Removed recommendation '{title}'" });
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

    private static string? GetField(PlanYaml plan, string planFolder, string field)
    {
        return field.ToLower() switch
        {
            "state" => plan.State,
            "project" => plan.Project,
            "level" => plan.Level,
            "title" => plan.Title,
            "created" => plan.Created.ToString("O"),
            "updated" => plan.Updated.ToString("O"),
            "executionprofile" => plan.ExecutionProfile,
            "initialprompt" => plan.InitialPrompt,
            "sourceurl" => plan.SourceUrl,
            "priority" => plan.Priority.ToString(),
            _ => null
        };
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
                title = r.Title, description = r.Description, state = r.State,
                impact = r.Impact, risk = r.Risk
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

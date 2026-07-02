using Ivy.Tendril.Models;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.Plans;
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
    private readonly IConfigService _configService;
    private readonly IGithubService _githubService;

    public PlanController(IPlanWatcherService planWatcher, IConfigService configService, IGithubService githubService)
    {
        _planWatcher = planWatcher;
        _configService = configService;
        _githubService = githubService;
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

            var status = VerificationStatusExtensions.Parse(request.Status);
            if (verification != null)
                verification.Status = status;
            else
                plan.Verifications.Add(new PlanVerificationEntry { Name = request.Name, Status = status });

            return (true, $"Set verification '{request.Name}' to '{status}'", 200);
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
                State = RecommendationStatus.Pending,
                Impact = request.Impact
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

            rec.State = string.IsNullOrEmpty(request?.Notes) ? RecommendationStatus.Accepted : RecommendationStatus.AcceptedWithNotes;
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

            rec.State = RecommendationStatus.Declined;
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

    [HttpPut("{planId}/recommendations/{title}")]
    public IActionResult SetRecField(string planId, string title, [FromBody] SetRecFieldRequest request)
    {
        var validFields = new[] { "title", "description", "state", "impact", "declinereason" };
        if (!validFields.Contains(request.Field.ToLower()))
            return BadRequest(new { error = $"Unknown field: {request.Field}. Valid: {string.Join(", ", validFields)}" });

        return ModifyPlanEndpoint(planId, plan =>
        {
            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (rec == null)
                return (false, $"Recommendation '{title}' not found", 404);

            switch (request.Field.ToLower())
            {
                case "title": rec.Title = request.Value; break;
                case "description": rec.Description = request.Value; break;
                case "state": rec.State = request.Value; break;
                case "impact": rec.Impact = request.Value; break;
                case "declinereason": rec.DeclineReason = request.Value; break;
            }

            return (true, $"Updated recommendation field '{request.Field}'", 200);
        });
    }

    [HttpPost]
    public IActionResult CreatePlanDirect([FromBody] CreatePlanDirectRequest request)
    {
        try
        {
            var resolvedProject = PlanProjectResolver.ResolveProject(request.Project, _configService.Projects);
            PlanSourceProjectGuard.EnsureSourceUrlMatchesProject(request.SourceUrl, resolvedProject, _githubService);

            var plansDir = PlanCommandHelpers.GetPlansDirectory();
            var planId = PlanYamlHelper.AllocatePlanId(plansDir);
            var safeTitle = PlanYamlHelper.ToSafeTitle(request.Title);
            var folderName = $"{planId}-{safeTitle}";
            var planFolder = Path.Combine(plansDir, folderName);

            Directory.CreateDirectory(planFolder);
            FileHelper.GrantBroadWriteAccess(planFolder);

            var plan = new PlanYaml
            {
                State = nameof(PlanStatus.Draft),
                Project = resolvedProject.Name,
                Level = request.Level ?? "Feature",
                Title = request.Title,
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow,
                InitialPrompt = request.InitialPrompt,
                SourceUrl = request.SourceUrl,
                ExecutionProfile = request.ExecutionProfile,
                Priority = 0
            };

            foreach (var repoPath in resolvedProject.RepoPaths)
                plan.Repos.Add(repoPath);

            // Seed the full project verification set (Required=Pending, Optional=Skipped);
            // names passed in the request override that default to Pending.
            var verificationOverrides = new Dictionary<string, VerificationStatus>(StringComparer.OrdinalIgnoreCase);
            if (request.Verifications != null)
                foreach (var v in request.Verifications)
                    verificationOverrides[v] = VerificationStatus.Pending;
            PlanCommandHelpers.ApplyProjectVerifications(plan, resolvedProject, verificationOverrides);

            if (request.RelatedPlans != null)
                foreach (var rp in request.RelatedPlans)
                    plan.RelatedPlans.Add(PlanCommandHelpers.ResolvePlanFolderName(rp));

            if (request.DependsOn != null)
                foreach (var dep in request.DependsOn)
                    plan.DependsOn.Add(PlanCommandHelpers.ResolvePlanFolderName(dep));

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            return Ok(new { id = planId, folder = planFolder, title = plan.Title });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{planId}/revisions")]
    public IActionResult WriteRevision(string planId, [FromBody] WriteRevisionRequest request)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var filePath = RevisionWriter.WriteNext(planFolder, request.Content, _configService);
            return Ok(new { file = Path.GetFileName(filePath), path = filePath });
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

    [HttpPost("{planId}/related-plans")]
    public IActionResult AddRelatedPlan(string planId, [FromBody] AddRelatedPlanRequest request)
    {
        string resolved;
        try { resolved = PlanCommandHelpers.ResolvePlanFolderName(request.RelatedPlan); }
        catch (DirectoryNotFoundException) { return NotFound(new { error = $"Referenced plan '{request.RelatedPlan}' not found" }); }

        return ModifyPlanEndpoint(planId, plan =>
        {
            if (plan.RelatedPlans.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                return (true, $"Related plan already present: {resolved}", 200);

            plan.RelatedPlans.Add(resolved);
            return (true, $"Added related plan: {resolved}", 200);
        });
    }

    [HttpDelete("{planId}/related-plans")]
    public IActionResult RemoveRelatedPlan(string planId, [FromBody] RemoveRelatedPlanRequest request)
    {
        string resolved;
        try { resolved = PlanCommandHelpers.ResolvePlanFolderName(request.RelatedPlan); }
        catch (DirectoryNotFoundException) { return NotFound(new { error = $"Referenced plan '{request.RelatedPlan}' not found" }); }

        return ModifyPlanEndpoint(planId, plan =>
        {
            var removed = plan.RelatedPlans.RemoveAll(r => r.Equals(resolved, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return (false, $"Related plan not found: {resolved}", 404);

            return (true, $"Removed related plan: {resolved}", 200);
        });
    }

    [HttpPost("{planId}/depends-on")]
    public IActionResult AddDependsOn(string planId, [FromBody] AddDependsOnRequest request)
    {
        string resolved;
        try { resolved = PlanCommandHelpers.ResolvePlanFolderName(request.DependsOn); }
        catch (DirectoryNotFoundException) { return NotFound(new { error = $"Referenced plan '{request.DependsOn}' not found" }); }

        return ModifyPlanEndpoint(planId, plan =>
        {
            if (plan.DependsOn.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                return (true, $"Dependency already present: {resolved}", 200);

            plan.DependsOn.Add(resolved);
            return (true, $"Added dependency: {resolved}", 200);
        });
    }

    [HttpDelete("{planId}/depends-on")]
    public IActionResult RemoveDependsOn(string planId, [FromBody] RemoveDependsOnRequest request)
    {
        string resolved;
        try { resolved = PlanCommandHelpers.ResolvePlanFolderName(request.DependsOn); }
        catch (DirectoryNotFoundException) { return NotFound(new { error = $"Referenced plan '{request.DependsOn}' not found" }); }

        return ModifyPlanEndpoint(planId, plan =>
        {
            var removed = plan.DependsOn.RemoveAll(d => d.Equals(resolved, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return (false, $"Dependency not found: {resolved}", 404);

            return (true, $"Removed dependency: {resolved}", 200);
        });
    }

    [HttpPost("{planId}/validate")]
    public IActionResult ValidatePlan(string planId)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            PlanValidationService.Validate(plan);
            return Ok(new { valid = true, message = "Plan is valid" });
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new { error = $"Plan '{planId}' not found" });
        }
        catch (ArgumentException ex)
        {
            return Ok(new { valid = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{planId}/verifications")]
    public IActionResult ListVerifications(string planId, [FromQuery] string? status = null)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(planId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            var verifications = plan.Verifications;

            if (!string.IsNullOrEmpty(status))
                verifications = verifications.Where(v => v.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();

            return Ok(verifications.Select(v => new { name = v.Name, status = v.Status }));
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

    [HttpPost("{planId}/verifications/add")]
    public IActionResult AddVerification(string planId, [FromBody] AddVerificationRequest request) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            if (plan.Verifications.Any(v => v.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
                return (false, $"Verification '{request.Name}' already exists", 409);

            plan.Verifications.Add(new PlanVerificationEntry
            {
                Name = request.Name,
                Status = request.Status != null ? VerificationStatusExtensions.Parse(request.Status) : VerificationStatus.Pending
            });

            return (true, $"Added verification '{request.Name}'", 200);
        });

    [HttpDelete("{planId}/verifications/{name}")]
    public IActionResult RemoveVerification(string planId, string name) =>
        ModifyPlanEndpoint(planId, plan =>
        {
            var match = plan.Verifications.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return (false, $"Verification '{name}' not found", 404);

            plan.Verifications.Remove(match);
            return (true, $"Removed verification '{name}'", 200);
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
                impact = r.Impact
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
public record AddRecRequest(string Title, string? Description = null, string? Impact = null);
public record AcceptRecRequest(string? Notes = null);
public record DeclineRecRequest(string? Reason = null);
public record CreatePlanDirectRequest(
    string Title,
    string Project,
    string? Level = null,
    string? InitialPrompt = null,
    string? ExecutionProfile = null,
    string? SourceUrl = null,
    List<string>? Verifications = null,
    List<string>? RelatedPlans = null,
    List<string>? DependsOn = null);
public record WriteRevisionRequest(string Content);
public record AddRelatedPlanRequest(string RelatedPlan);
public record RemoveRelatedPlanRequest(string RelatedPlan);
public record AddDependsOnRequest(string DependsOn);
public record RemoveDependsOnRequest(string DependsOn);
public record AddVerificationRequest(string Name, string? Status = null);
public record SetRecFieldRequest(string Field, string Value);

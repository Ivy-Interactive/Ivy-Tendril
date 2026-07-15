using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Plugins;

internal class TendrilApi(IJobService jobService, IConfigService configService) : ITendrilApi
{
    public string StartCreatePlan(string description, string? project = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required", nameof(description));
        return jobService.StartJob(new CreatePlanArgs(description, project ?? "Auto"));
    }

    public string StartExecutePlan(string planId, string? note = null)
    {
        var folderPath = PlanCommandHelpers.ResolvePlanFolder(planId);
        return jobService.StartJob(new ExecutePlanArgs(folderPath, note));
    }

    public TendrilJobStatus? GetJob(string jobId)
    {
        var job = jobService.GetJob(jobId);
        return job == null
            ? null
            : new TendrilJobStatus(job.Id, job.Type, job.Status.ToString(), job.StatusMessage, job.PlanFile);
    }

    public IReadOnlyList<TendrilPlanSummary> ListPlans(string? state = null, string? project = null, int limit = 20)
    {
        var results = new List<TendrilPlanSummary>();
        var plansDir = PlanCommandHelpers.GetPlansDirectory();
        if (!Directory.Exists(plansDir))
            return results;

        foreach (var dir in Directory.GetDirectories(plansDir).OrderByDescending(Path.GetFileName))
        {
            var folderName = Path.GetFileName(dir);
            var dash = folderName.IndexOf('-');
            if (dash <= 0 || !int.TryParse(folderName[..dash], out _)) continue;

            PlanYaml yaml;
            try { yaml = PlanCommandHelpers.ReadPlan(dir); }
            catch { continue; }

            if (!string.IsNullOrEmpty(state) &&
                !string.Equals(yaml.State, state, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrEmpty(project) &&
                !string.Equals(yaml.Project, project, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new TendrilPlanSummary(folderName[..dash], yaml.Title, yaml.State, yaml.Project, yaml.Level));
            if (results.Count >= limit) break;
        }

        return results;
    }

    public IReadOnlyList<string> ListProjects() =>
        configService.Settings.Projects.Select(p => p.Name).ToList();
}

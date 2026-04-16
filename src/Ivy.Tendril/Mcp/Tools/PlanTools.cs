using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ivy.Tendril.Mcp.Tools;

[McpServerToolType]
public sealed class PlanTools(McpAuthenticationService authService)
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly Regex FolderNameRegex = new(@"^(\d{5})-(.+)$", RegexOptions.Compiled);

    [McpServerTool(Name = "tendril_get_plan"), Description("Fetch plan metadata by ID or folder path")]
    public string GetPlan(
        [Description("Plan ID (e.g., '03228') or full folder path")] string planId)
    {
        if (!authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        var plansDir = GetPlansDirectory();
        if (plansDir == null)
            return "Error: TENDRIL_HOME is not set.";

        var planFolder = ResolvePlanFolder(plansDir, planId);
        if (planFolder == null)
            return $"Error: Plan '{planId}' not found.";

        return ReadPlanSummary(planFolder);
    }

    [McpServerTool(Name = "tendril_list_plans"), Description("Query plans by state, project, or date range")]
    public string ListPlans(
        [Description("Filter by plan state (e.g., Draft, Executing, ReadyForReview, Failed, Completed)")] string? state = null,
        [Description("Filter by project name")] string? project = null,
        [Description("Filter plans created after this date (ISO 8601, e.g., 2026-04-01)")] string? since = null)
    {
        if (!authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        var plansDir = GetPlansDirectory();
        if (plansDir == null)
            return "Error: TENDRIL_HOME is not set.";

        if (!Directory.Exists(plansDir))
            return "Error: Plans directory does not exist.";

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

            var yaml = ReadPlanYaml(dir);
            if (yaml == null) continue;

            if (!string.IsNullOrEmpty(state) &&
                !string.Equals(yaml.State, state, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(project) &&
                !string.Equals(yaml.Project, project, StringComparison.OrdinalIgnoreCase))
                continue;

            if (sinceDate.HasValue && yaml.Created < sinceDate.Value)
                continue;

            var id = match.Groups[1].Value;
            sb.AppendLine($"- [{id}] {yaml.Title} | State: {yaml.State} | Project: {yaml.Project} | Level: {yaml.Level}");
            count++;

            if (count >= 50)
            {
                sb.AppendLine($"... (showing first 50 of potentially more results)");
                break;
            }
        }

        if (count == 0)
            return "No plans found matching the specified criteria.";

        sb.Insert(0, $"Found {count} plan(s):\n");
        return sb.ToString();
    }

    [McpServerTool(Name = "tendril_inbox"), Description("Create a new plan by writing to the Tendril inbox")]
    public string CreatePlan(
        [Description("Plan title/description")] string title,
        [Description("Project name (optional)")] string? project = null,
        [Description("Priority level: Critical, Important, NiceToHave (optional)")] string? level = null,
        [Description("Detailed prompt/description for plan creation (optional)")] string? prompt = null)
    {
        if (!authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        var tendrilHome = GetTendrilHome();
        if (tendrilHome == null)
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

    [McpServerTool(Name = "tendril_transition_plan"), Description("Transition a plan to a different state")]
    public string TransitionPlan(
        [Description("Plan ID (e.g., '03228') or full folder path")] string planId,
        [Description("Target state: Draft, Building, Updating, Executing, Completed, Failed, ReadyForReview, Skipped, Icebox, Blocked")] string targetState)
    {
        if (!authService.ValidateEnvironmentToken())
            return "Error: Authentication failed. Access denied.";

        var plansDir = GetPlansDirectory();
        if (plansDir == null)
            return "Error: TENDRIL_HOME is not set.";

        var planFolder = ResolvePlanFolder(plansDir, planId);
        if (planFolder == null)
            return $"Error: Plan '{planId}' not found.";

        // Validate target state
        var validStates = new[] { "Draft", "Building", "Updating", "Executing", "Completed",
                                  "Failed", "ReadyForReview", "Skipped", "Icebox", "Blocked" };
        if (!validStates.Contains(targetState, StringComparer.OrdinalIgnoreCase))
            return $"Error: Invalid state '{targetState}'. Valid states: {string.Join(", ", validStates)}";

        // Read current plan.yaml
        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        if (!File.Exists(planYamlPath))
            return $"Error: plan.yaml not found in {planFolder}";

        try
        {
            var yaml = File.ReadAllText(planYamlPath);
            var planYaml = YamlDeserializer.Deserialize<PlanYamlDto>(yaml);
            if (planYaml == null)
                return "Error: Failed to parse plan.yaml";

            var oldState = planYaml.State;

            // Update state and timestamp
            planYaml.State = validStates.First(s => s.Equals(targetState, StringComparison.OrdinalIgnoreCase));
            planYaml.Updated = DateTime.UtcNow;

            // Serialize and write back
            var updatedYaml = YamlSerializer.Serialize(planYaml);
            File.WriteAllText(planYamlPath, updatedYaml);

            var folderName = Path.GetFileName(planFolder);
            return $"Successfully transitioned plan {folderName} from {oldState} to {planYaml.State}";
        }
        catch (Exception ex)
        {
            return $"Error: Failed to update plan state: {ex.Message}";
        }
    }

    private static string? GetTendrilHome()
    {
        var home = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (!string.IsNullOrEmpty(home) && home.StartsWith('"') && home.EndsWith('"'))
            home = home[1..^1];
        return string.IsNullOrEmpty(home) ? null : home;
    }

    private static string? GetPlansDirectory()
    {
        var plans = Environment.GetEnvironmentVariable("TENDRIL_PLANS")?.Trim();
        if (!string.IsNullOrEmpty(plans))
            return plans;
        var home = GetTendrilHome();
        return home == null ? null : Path.Combine(home, "Plans");
    }

    private static string? ResolvePlanFolder(string plansDir, string planId)
    {
        if (Directory.Exists(planId))
            return planId;

        var paddedId = planId.PadLeft(5, '0');
        var matching = Directory.GetDirectories(plansDir, $"{paddedId}-*");
        return matching.Length > 0 ? matching[0] : null;
    }

    private static PlanYamlDto? ReadPlanYaml(string planFolder)
    {
        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        if (!File.Exists(planYamlPath)) return null;

        try
        {
            var content = File.ReadAllText(planYamlPath);
            return YamlDeserializer.Deserialize<PlanYamlDto>(content);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadPlanSummary(string planFolder)
    {
        var yaml = ReadPlanYaml(planFolder);
        if (yaml == null)
            return $"Error: Could not parse plan.yaml in {planFolder}";

        var folderName = Path.GetFileName(planFolder);
        var match = FolderNameRegex.Match(folderName);
        var id = match.Success ? match.Groups[1].Value : folderName;

        var sb = new StringBuilder();
        sb.AppendLine($"# Plan {id}: {yaml.Title}");
        sb.AppendLine();
        sb.AppendLine($"- **State:** {yaml.State}");
        sb.AppendLine($"- **Project:** {yaml.Project}");
        sb.AppendLine($"- **Level:** {yaml.Level}");
        sb.AppendLine($"- **Created:** {yaml.Created:O}");
        sb.AppendLine($"- **Updated:** {yaml.Updated:O}");

        if (yaml.Repos is { Count: > 0 })
            sb.AppendLine($"- **Repos:** {string.Join(", ", yaml.Repos)}");

        if (yaml.Commits is { Count: > 0 })
            sb.AppendLine($"- **Commits:** {string.Join(", ", yaml.Commits)}");

        if (yaml.Prs is { Count: > 0 })
            sb.AppendLine($"- **PRs:** {string.Join(", ", yaml.Prs)}");

        if (!string.IsNullOrEmpty(yaml.InitialPrompt))
        {
            sb.AppendLine();
            sb.AppendLine($"## Initial Prompt");
            sb.AppendLine(yaml.InitialPrompt);
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

    internal class PlanYamlDto
    {
        public string State { get; set; } = "";
        public string Project { get; set; } = "";
        public string Level { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string>? Repos { get; set; }
        public List<string>? Commits { get; set; }
        public List<string>? Prs { get; set; }
        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
        public string? InitialPrompt { get; set; }
        public string? SourceUrl { get; set; }
        public string? SessionId { get; set; }
        public List<string>? DependsOn { get; set; }
        public List<string>? RelatedPlans { get; set; }
        public List<VerificationEntry>? Verifications { get; set; }
        public int Priority { get; set; }
        public string? ExecutionProfile { get; set; }
    }

    internal class VerificationEntry
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "Pending";
    }
}

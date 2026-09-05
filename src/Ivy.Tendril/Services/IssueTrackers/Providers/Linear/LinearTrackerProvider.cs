using System.Text;
using System.Text.Json;
using Ivy.Tendril.Services.IssueTrackers.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.IssueTrackers.Providers.Linear;

public class LinearTrackerProvider(
    IConfigService config,
    IHttpClientFactory httpClientFactory,
    ILogger<LinearTrackerProvider> logger) : IIssueTrackerProvider
{
    private const string GraphQLEndpoint = "https://api.linear.app/graphql";
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Linear");

    public string ProviderId => "linear";
    public string DisplayName => "Linear";
    public Icons Icon => Icons.Layers;

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var hasConnections = config.Settings.TrackerConnections.Any(c => c.Provider == "linear" && !string.IsNullOrWhiteSpace(c.ApiKey));
        if (hasConnections) return Task.FromResult(true);

        var apiKey = ResolveApiKey();
        return Task.FromResult(!string.IsNullOrWhiteSpace(apiKey));
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetMyAssignedIssuesAsync(CancellationToken ct = default)
    {
        return await GetMyAssignedIssuesAsync(connection: null, ct);
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetMyAssignedIssuesAsync(
        TrackerConnectionConfig? connection,
        CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey(connection);
        if (string.IsNullOrWhiteSpace(apiKey))
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure("Linear API key is not configured.", []);

        const string query = """
            query MyAssignedIssues {
              viewer {
                assignedIssues(filter: { state: { type: { nin: ["completed", "canceled"] } } }, first: 100) {
                  nodes {
                    id
                    identifier
                    title
                    description
                    url
                    priority
                    updatedAt
                    state { name type }
                    labels { nodes { name } }
                    assignee { displayName name }
                    team { key name }
                  }
                }
              }
            }
            """;

        return await ExecuteGraphQLAsync(apiKey, query, variables: null, "viewer.assignedIssues.nodes", ct);
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesAsync(
        ProjectConfig project,
        TrackerIssueQuery query,
        CancellationToken ct = default)
    {
        var tracker = project.IssueTrackers.FirstOrDefault(t => t.Provider == "linear") ?? project.IssueTracker;
        return await GetProjectIssuesForTrackerAsync(project, tracker ?? new ProjectTrackerConfig { Provider = "linear" }, query, ct);
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesForTrackerAsync(
        ProjectConfig project,
        ProjectTrackerConfig tracker,
        TrackerIssueQuery query,
        CancellationToken ct = default)
    {
        var connection = !string.IsNullOrEmpty(tracker.ConnectionId)
            ? config.Settings.TrackerConnections.FirstOrDefault(c => c.Id == tracker.ConnectionId)
            : config.Settings.TrackerConnections.FirstOrDefault(c => c.Provider == "linear");

        var apiKey = ResolveApiKey(connection);
        if (string.IsNullOrWhiteSpace(apiKey))
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure("Linear API key is not configured.", []);

        var teamKey = tracker.TeamKey ?? project.IssueTracker?.TeamKey ?? project.GetMeta("linear_team") ?? project.GetMeta("linear_key");
        if (string.IsNullOrWhiteSpace(teamKey))
        {
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(
                $"No Linear team key configured for project {project.Name}.", []);
        }

        const string gql = """
            query TeamIssues($teamKey: String!) {
              team(id: $teamKey) {
                issues(filter: { state: { type: { nin: ["completed", "canceled"] } } }, first: 100) {
                  nodes {
                    id
                    identifier
                    title
                    description
                    url
                    priority
                    updatedAt
                    state { name type }
                    labels { nodes { name } }
                    assignee { displayName name }
                    team { key name }
                  }
                }
              }
            }
            """;

        var variables = new { teamKey };
        return await ExecuteGraphQLAsync(apiKey, gql, variables, "team.issues.nodes", ct);
    }

    private async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> ExecuteGraphQLAsync(
        string apiKey,
        string query,
        object? variables,
        string nodesJsonPath,
        CancellationToken ct)
    {
        try
        {
            var payload = new { query, variables };
            var json = JsonSerializer.Serialize(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, GraphQLEndpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", apiKey);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Linear GraphQL error {StatusCode}: {Error}", response.StatusCode, err);
                return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(
                    $"Linear GraphQL error ({response.StatusCode})", []);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("errors", out var errorsProp) && errorsProp.GetArrayLength() > 0)
            {
                var firstError = errorsProp[0].GetProperty("message").GetString() ?? "Linear GraphQL error";
                return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(firstError, []);
            }

            var data = doc.RootElement.GetProperty("data");
            var nodesElement = TraversePath(data, nodesJsonPath);

            if (!nodesElement.HasValue || nodesElement.Value.ValueKind != JsonValueKind.Array)
                return ProviderResult<IReadOnlyList<TrackerIssue>>.Success([]);

            var issuesList = new List<TrackerIssue>();
            foreach (var node in nodesElement.Value.EnumerateArray())
            {
                var identifier = node.GetProperty("identifier").GetString() ?? "";
                var title = node.GetProperty("title").GetString() ?? "";
                var description = node.TryGetProperty("description", out var dProp) ? dProp.GetString() : null;
                var url = node.TryGetProperty("url", out var uProp) ? uProp.GetString() : null;

                var stateName = "Open";
                if (node.TryGetProperty("state", out var stateProp) && stateProp.TryGetProperty("name", out var sn))
                {
                    stateName = sn.GetString() ?? "Open";
                }

                string? priorityStr = null;
                if (node.TryGetProperty("priority", out var pProp) && pProp.TryGetInt32(out var pNum))
                {
                    priorityStr = pNum switch
                    {
                        1 => "Urgent",
                        2 => "High",
                        3 => "Medium",
                        4 => "Low",
                        _ => null
                    };
                }

                var labels = new List<string>();
                if (node.TryGetProperty("labels", out var labelsWrapper) &&
                    labelsWrapper.TryGetProperty("nodes", out var labelsNodes))
                {
                    foreach (var l in labelsNodes.EnumerateArray())
                    {
                        if (l.TryGetProperty("name", out var ln) && ln.GetString() is { } name)
                            labels.Add(name);
                    }
                }

                var assignees = new List<string>();
                if (node.TryGetProperty("assignee", out var assObj) && assObj.ValueKind == JsonValueKind.Object)
                {
                    if (assObj.TryGetProperty("displayName", out var dn) && dn.GetString() is { } name)
                        assignees.Add(name);
                    else if (assObj.TryGetProperty("name", out var n) && n.GetString() is { } name2)
                        assignees.Add(name2);
                }

                string? teamKey = null;
                if (node.TryGetProperty("team", out var teamObj) && teamObj.TryGetProperty("key", out var tk))
                {
                    teamKey = tk.GetString();
                }

                DateTimeOffset? updatedAt = null;
                if (node.TryGetProperty("updatedAt", out var upProp) &&
                    DateTimeOffset.TryParse(upProp.GetString(), out var parsedDate))
                {
                    updatedAt = parsedDate;
                }

                issuesList.Add(new TrackerIssue(
                    Id: $"linear:{identifier}",
                    Key: identifier,
                    Title: title,
                    Body: description,
                    Labels: labels.ToArray(),
                    Assignees: assignees.ToArray(),
                    Scope: teamKey,
                    Url: url,
                    ProviderId: ProviderId,
                    Status: stateName,
                    Priority: priorityStr,
                    UpdatedAt: updatedAt
                ));
            }

            return ProviderResult<IReadOnlyList<TrackerIssue>>.Success(issuesList);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Linear query failed");
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(ex.Message, []);
        }
    }

    private static JsonElement? TraversePath(JsonElement root, string path)
    {
        var segments = path.Split('.');
        var current = root;
        foreach (var seg in segments)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(seg, out var next))
                return null;
            current = next;
        }
        return current;
    }

    private string? ResolveApiKey(TrackerConnectionConfig? connection = null)
    {
        if (connection != null && connection.Provider == "linear" && !string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            return connection.ApiKey;
        }

        return config.Settings.IssueTrackers?.Linear?.ApiKey ??
               Environment.GetEnvironmentVariable("LINEAR_API_KEY");
    }
}

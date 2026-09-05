using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Services.IssueTrackers.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.IssueTrackers.Providers.Jira;

public class JiraTrackerProvider(
    IConfigService config,
    IHttpClientFactory httpClientFactory,
    ILogger<JiraTrackerProvider> logger) : IIssueTrackerProvider
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Jira");
    public string ProviderId => "jira";
    public string DisplayName => "Jira";
    public Icons Icon => Icons.SquareCheck;

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var (url, email, token) = ResolveCredentials();
        return Task.FromResult(!string.IsNullOrWhiteSpace(url) && (!string.IsNullOrWhiteSpace(token) || !string.IsNullOrWhiteSpace(email)));
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetMyAssignedIssuesAsync(CancellationToken ct = default)
    {
        var (url, email, token) = ResolveCredentials();
        if (string.IsNullOrWhiteSpace(url))
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure("Jira URL is not configured.", []);

        var jql = "assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC";
        return await ExecuteSearchAsync(url, email, token, jql, limit: 100, scope: null, ct);
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesAsync(
        ProjectConfig project,
        TrackerIssueQuery query,
        CancellationToken ct = default)
    {
        var (url, email, token) = ResolveCredentials();
        if (string.IsNullOrWhiteSpace(url))
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure("Jira URL is not configured.", []);

        var projectKey = project.IssueTracker?.ProjectKey ?? project.GetMeta("jira_project") ?? project.GetMeta("jira_key");
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(
                $"No Jira project key configured for project {project.Name}. Set issueTracker.projectKey in config.yaml.", []);
        }

        var jql = $"project = \"{projectKey}\" AND statusCategory != Done ORDER BY updated DESC";
        return await ExecuteSearchAsync(url, email, token, jql, limit: query.Limit, scope: projectKey, ct);
    }

    private async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> ExecuteSearchAsync(
        string baseUrl,
        string? email,
        string? token,
        string jql,
        int limit,
        string? scope,
        CancellationToken ct)
    {
        try
        {
            baseUrl = baseUrl.TrimEnd('/');
            var searchEndpoint = $"{baseUrl}/rest/api/3/search";

            var requestBody = new
            {
                jql = jql,
                maxResults = limit,
                fields = new[] { "summary", "description", "status", "priority", "labels", "assignee", "updated", "project" }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, searchEndpoint)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            ApplyAuth(request, email, token);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Jira API returned {StatusCode}: {Error}", response.StatusCode, errorBody);
                return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(
                    $"Jira API error ({response.StatusCode}): {Truncate(errorBody, 150)}", []);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);

            var issuesList = new List<TrackerIssue>();
            if (doc.RootElement.TryGetProperty("issues", out var issuesArray))
            {
                foreach (var issueElem in issuesArray.EnumerateArray())
                {
                    var key = issueElem.GetProperty("key").GetString() ?? "";
                    var fields = issueElem.GetProperty("fields");

                    var summary = fields.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() ?? "" : "";

                    string? body = null;
                    if (fields.TryGetProperty("description", out var descProp))
                    {
                        body = descProp.ValueKind switch
                        {
                            JsonValueKind.String => descProp.GetString(),
                            JsonValueKind.Object => AdfToMarkdownConverter.Convert(descProp.GetRawText()),
                            _ => null
                        };
                    }

                    var status = "Open";
                    if (fields.TryGetProperty("status", out var statusProp) &&
                        statusProp.TryGetProperty("name", out var sName))
                    {
                        status = sName.GetString() ?? "Open";
                    }

                    string? priority = null;
                    if (fields.TryGetProperty("priority", out var prioProp) &&
                        prioProp.TryGetProperty("name", out var pName))
                    {
                        priority = pName.GetString();
                    }

                    var labels = new List<string>();
                    if (fields.TryGetProperty("labels", out var labelsArray))
                    {
                        foreach (var l in labelsArray.EnumerateArray())
                        {
                            if (l.GetString() is { } ls && !string.IsNullOrWhiteSpace(ls))
                                labels.Add(ls);
                        }
                    }

                    var assignees = new List<string>();
                    if (fields.TryGetProperty("assignee", out var assigneeObj) && assigneeObj.ValueKind == JsonValueKind.Object)
                    {
                        if (assigneeObj.TryGetProperty("displayName", out var dn) && dn.GetString() is { } name)
                            assignees.Add(name);
                    }

                    var issueScope = scope;
                    if (issueScope == null && fields.TryGetProperty("project", out var projObj) &&
                        projObj.TryGetProperty("key", out var pk))
                    {
                        issueScope = pk.GetString();
                    }

                    DateTimeOffset? updatedAt = null;
                    if (fields.TryGetProperty("updated", out var upProp) &&
                        DateTimeOffset.TryParse(upProp.GetString(), out var parsedDate))
                    {
                        updatedAt = parsedDate;
                    }

                    var webUrl = $"{baseUrl}/browse/{key}";

                    issuesList.Add(new TrackerIssue(
                        Id: $"jira:{key}",
                        Key: key,
                        Title: summary,
                        Body: body,
                        Labels: labels.ToArray(),
                        Assignees: assignees.ToArray(),
                        Scope: issueScope,
                        Url: webUrl,
                        ProviderId: ProviderId,
                        Status: status,
                        Priority: priority,
                        UpdatedAt: updatedAt
                    ));
                }
            }

            return ProviderResult<IReadOnlyList<TrackerIssue>>.Success(issuesList);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Jira search failed for JQL: {Jql}", jql);
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(ex.Message, []);
        }
    }

    private (string? url, string? email, string? token) ResolveCredentials()
    {
        var settings = config.Settings.IssueTrackers?.Jira;
        var url = settings?.Url ?? Environment.GetEnvironmentVariable("JIRA_URL");
        var email = settings?.Email ?? Environment.GetEnvironmentVariable("JIRA_EMAIL");
        var token = settings?.ApiToken ?? Environment.GetEnvironmentVariable("JIRA_API_TOKEN");

        return (url, email, token);
    }

    private static void ApplyAuth(HttpRequestMessage request, string? email, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        if (!string.IsNullOrWhiteSpace(email))
        {
            var raw = $"{email}:{token}";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}

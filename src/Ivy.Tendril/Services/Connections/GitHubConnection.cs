using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ivy.Tendril.Services.Connections;

public class GitHubConnection : IConnectionProvider
{
    public string ProviderName => "GitHub";
    public string Description => "Allow agents to securely open pull requests and comment on PRs.";
    public string Icon => "Github";

    public async Task<(bool Success, string ErrorMessage)> TestConnectionAsync(string connectionString, HttpClient client)
    {
        try
        {
            var config = ParseConfig(connectionString);
            if (!config.TryGetValue("Token", out var token) || string.IsNullOrWhiteSpace(token))
                return (false, "Missing 'Token' in connection configuration.");

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd("Ivy-Tendril");
            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return (true, "");

            var body = await response.Content.ReadAsStringAsync();
            return (false, $"GitHub Auth Fail: Status {response.StatusCode}, Body: {body}");
        }
        catch (Exception ex)
        {
            return (false, $"Connection Error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Result)> ExecuteActionAsync(string connectionString, string action, string argsJson, HttpClient client)
    {
        try
        {
            var config = ParseConfig(connectionString);
            if (!config.TryGetValue("Token", out var token) || string.IsNullOrWhiteSpace(token))
                return (false, "Missing 'Token' in connection configuration.");

            var args = ParseConfig(argsJson);

            if (action.Equals("create-pr", StringComparison.OrdinalIgnoreCase))
            {
                if (!args.TryGetValue("repo", out var repo) || string.IsNullOrWhiteSpace(repo))
                    return (false, "Missing required parameter 'repo' (owner/name).");
                if (!args.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title))
                    return (false, "Missing required parameter 'title'.");
                if (!args.TryGetValue("head", out var head) || string.IsNullOrWhiteSpace(head))
                    return (false, "Missing required parameter 'head'.");
                if (!args.TryGetValue("base", out var @base) || string.IsNullOrWhiteSpace(@base))
                    return (false, "Missing required parameter 'base'.");
                args.TryGetValue("body", out var bodyVal);

                var payload = new { title, head, @base, body = bodyVal ?? "" };
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repo}/pulls");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.UserAgent.ParseAdd("Ivy-Tendril");
                request.Content = JsonContent.Create(payload);
                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return (true, body);
                return (false, $"GitHub create-pr failed (Status {response.StatusCode}): {body}");
            }
            else if (action.Equals("comment-pr", StringComparison.OrdinalIgnoreCase))
            {
                if (!args.TryGetValue("repo", out var repo) || string.IsNullOrWhiteSpace(repo))
                    return (false, "Missing required parameter 'repo' (owner/name).");
                if (!args.TryGetValue("prNumber", out var prNumStr) && !args.TryGetValue("number", out prNumStr) || string.IsNullOrWhiteSpace(prNumStr))
                    return (false, "Missing required parameter 'prNumber' (or 'number').");
                if (!args.TryGetValue("body", out var commentBody) || string.IsNullOrWhiteSpace(commentBody))
                    return (false, "Missing required parameter 'body'.");

                var payload = new { body = commentBody };
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repo}/issues/{prNumStr}/comments");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.UserAgent.ParseAdd("Ivy-Tendril");
                request.Content = JsonContent.Create(payload);
                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return (true, body);
                return (false, $"GitHub comment-pr failed (Status {response.StatusCode}): {body}");
            }
            else
            {
                return (false, $"Unknown GitHub action: {action}. Supported: create-pr, comment-pr");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Execution Error: {ex.Message}");
        }
    }

    private static Dictionary<string, string> ParseConfig(string json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return dict;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        dict[prop.Name] = prop.Value.GetString() ?? "";
                    else
                        dict[prop.Name] = prop.Value.GetRawText();
                }
            }
        }
        catch
        {
            dict["Token"] = json.Trim();
        }
        return dict;
    }
}

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ivy.Tendril.Services.Connections;

public class SlackConnection : IConnectionProvider
{
    public string ProviderName => "Slack";
    public string Description => "Connect Slack to post execution plans, update status, and receive alerts.";
    public string Icon => "Slack";

    public async Task<(bool Success, string ErrorMessage)> TestConnectionAsync(string connectionString, HttpClient client)
    {
        try
        {
            var config = ParseConfig(connectionString);
            if (!config.TryGetValue("Token", out var token) || string.IsNullOrWhiteSpace(token))
                return (false, "Missing 'Token' in connection configuration.");

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/auth.test");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
            {
                return (true, "");
            }
            
            return (false, $"Slack Auth Fail: {body}");
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

            if (action.Equals("send-message", StringComparison.OrdinalIgnoreCase))
            {
                if (!args.TryGetValue("channel", out var channel) || string.IsNullOrWhiteSpace(channel))
                    return (false, "Missing required parameter 'channel'.");
                if (!args.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text))
                    return (false, "Missing required parameter 'text'.");

                var payload = new { channel, text };
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/chat.postMessage");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = JsonContent.Create(payload);
                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
                    return (true, body);
                return (false, $"Slack send-message failed: {body}");
            }
            else if (action.Equals("add-reaction", StringComparison.OrdinalIgnoreCase))
            {
                if (!args.TryGetValue("channel", out var channel) || string.IsNullOrWhiteSpace(channel))
                    return (false, "Missing required parameter 'channel'.");
                if (!args.TryGetValue("timestamp", out var timestamp) && !args.TryGetValue("ts", out timestamp) || string.IsNullOrWhiteSpace(timestamp))
                    return (false, "Missing required parameter 'timestamp' (or 'ts').");
                if (!args.TryGetValue("reaction", out var name) && !args.TryGetValue("name", out name) || string.IsNullOrWhiteSpace(name))
                    return (false, "Missing required parameter 'reaction' (or 'name').");

                var payload = new { channel, timestamp, name };
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/reactions.add");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = JsonContent.Create(payload);
                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
                    return (true, body);
                return (false, $"Slack add-reaction failed: {body}");
            }
            else
            {
                return (false, $"Unknown Slack action: {action}. Supported: send-message, add-reaction");
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

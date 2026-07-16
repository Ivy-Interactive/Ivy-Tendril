using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ivy.Tendril.Services.Connections;

public class DiscordConnection : IConnectionProvider
{
    public string ProviderName => "Discord";

    public async Task<(bool Success, string ErrorMessage)> TestConnectionAsync(string connectionString, HttpClient client)
    {
        try
        {
            var config = ParseConfig(connectionString);
            if (!config.TryGetValue("Token", out var token) || string.IsNullOrWhiteSpace(token))
                return (false, "Missing 'Token' in connection configuration.");

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/users/@me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", token);
            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
                return (true, "");

            var body = await response.Content.ReadAsStringAsync();
            return (false, $"Discord Auth Fail: Status {response.StatusCode}, Body: {body}");
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
                    return (false, "Missing required parameter 'channel' (Discord channel ID).");
                if (!args.TryGetValue("text", out var text) && !args.TryGetValue("content", out text) || string.IsNullOrWhiteSpace(text))
                    return (false, "Missing required parameter 'text' (or 'content').");

                var payload = new { content = text };
                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://discord.com/api/v10/channels/{channel}/messages");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bot", token);
                request.Content = JsonContent.Create(payload);
                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return (true, body);
                return (false, $"Discord send-message failed (Status {response.StatusCode}): {body}");
            }
            else
            {
                return (false, $"Unknown Discord action: {action}. Supported: send-message");
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

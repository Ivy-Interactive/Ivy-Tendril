using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Helpers;

public static class LlmEndpointTester
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<ModelValidationResult> TestModelPromptAsync(
        string baseUrl,
        string apiKey,
        string model,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.AuthError,
                Model = model,
                ErrorMessage = "Base URL is not configured.",
            };
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ModelValidationResult
            {
                Status = ModelValidationStatus.AuthError,
                Model = model,
                ErrorMessage = "API key is not configured.",
            };
        }

        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var isAnthropic = normalizedBaseUrl.Contains("api.anthropic.com");

        var effectiveModel = model;
        if (string.IsNullOrWhiteSpace(effectiveModel) || effectiveModel.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedBaseUrl.Contains("llmproxy.ivy.app")) effectiveModel = "claude-opus-5";
            else if (isAnthropic) effectiveModel = "claude-sonnet-5";
            else if (normalizedBaseUrl.Contains("generativelanguage.googleapis.com") || normalizedBaseUrl.Contains("gemini") || normalizedBaseUrl.Contains("google")) effectiveModel = "gemini-3.7-flash";
            else if (normalizedBaseUrl.Contains("api.berget.ai")) effectiveModel = "moonshotai/Kimi-K3";
            else effectiveModel = "gpt-5.6-terra";
        }

        List<string> endpointsToTry = [];
        if (isAnthropic)
        {
            if (normalizedBaseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                endpointsToTry.Add($"{normalizedBaseUrl}/messages");
            else
                endpointsToTry.Add($"{normalizedBaseUrl}/v1/messages");
        }
        else
        {
            if (normalizedBaseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                endpointsToTry.Add($"{normalizedBaseUrl}/chat/completions");
            }
            else
            {
                endpointsToTry.Add($"{normalizedBaseUrl}/v1/chat/completions");
                endpointsToTry.Add($"{normalizedBaseUrl}/chat/completions");
            }
        }

        string? lastError = null;
        var lastStatus = ModelValidationStatus.Unknown;

        foreach (var endpoint in endpointsToTry)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

                if (isAnthropic)
                {
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");

                    var payload = new
                    {
                        model = effectiveModel,
                        max_tokens = 5,
                        messages = new[] { new { role = "user", content = "ping" } }
                    };
                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                }
                else
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    if (normalizedBaseUrl.Contains("ivy.app") || normalizedBaseUrl.Contains("llmproxy"))
                    {
                        request.Headers.Add("x-api-key", apiKey);
                    }

                    var payload = new
                    {
                        model = effectiveModel,
                        max_tokens = 5,
                        messages = new[] { new { role = "user", content = "ping" } }
                    };
                    request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                }

                using var response = await HttpClient.SendAsync(request, ct);
                var content = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    return new ModelValidationResult
                    {
                        Status = ModelValidationStatus.Ok,
                        Model = model,
                    };
                }

                var errorMsg = ExtractErrorMessage(content, (int)response.StatusCode);
                lastError = errorMsg;

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden ||
                    errorMsg.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("AuthenticationError", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                {
                    return new ModelValidationResult
                    {
                        Status = ModelValidationStatus.AuthError,
                        Model = model,
                        ErrorMessage = errorMsg,
                    };
                }

                if (errorMsg.Contains("model_not_found", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("Invalid model", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
                    errorMsg.Contains("not available", StringComparison.OrdinalIgnoreCase))
                {
                    lastStatus = ModelValidationStatus.InvalidModel;
                }
                else
                {
                    lastStatus = ModelValidationStatus.Unknown;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                lastStatus = ModelValidationStatus.Unknown;
            }
        }

        return new ModelValidationResult
        {
            Status = lastStatus,
            Model = model,
            ErrorMessage = lastError ?? "Model validation failed",
        };
    }

    private static string ExtractErrorMessage(string jsonOrText, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(jsonOrText))
            return $"HTTP {statusCode}";

        try
        {
            using var doc = JsonDocument.Parse(jsonOrText);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorProp))
            {
                if (errorProp.ValueKind == JsonValueKind.String)
                    return errorProp.GetString()!;
                if (errorProp.ValueKind == JsonValueKind.Object)
                {
                    if (errorProp.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                        return msgProp.GetString()!;
                    return errorProp.GetRawText();
                }
            }

            if (root.TryGetProperty("detail", out var detailProp))
            {
                if (detailProp.ValueKind == JsonValueKind.String)
                    return detailProp.GetString()!;
                return detailProp.GetRawText();
            }

            if (root.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
            {
                return messageProp.GetString()!;
            }
        }
        catch
        {
            // Not valid JSON, return text directly
        }

        return jsonOrText.Length > 200 ? jsonOrText[..200] + "..." : jsonOrText;
    }
}

using System.Text.Json.Nodes;

namespace Ivy.Tendril.Services.Agents;

public class CopilotOutputNormalizer : IOutputNormalizer
{
    private bool _initEmitted;

    public IReadOnlyList<string> Normalize(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("[stderr]"))
            return [rawLine];

        JsonNode? node;
        try { node = JsonNode.Parse(rawLine); }
        catch { return []; }

        if (node is null) return [];

        var type = node["type"]?.GetValue<string>();

        return type switch
        {
            "session.tools_updated" => HandleToolsUpdated(node),
            "assistant.message" => HandleAssistantMessage(node),
            "tool.execution_start" => HandleToolExecutionStart(node),
            "tool.execution_complete" => HandleToolExecutionComplete(node),
            "result" => HandleResult(node),
            _ => []
        };
    }

    public IReadOnlyList<string> Flush() => [];

    private IReadOnlyList<string> HandleToolsUpdated(JsonNode node)
    {
        if (_initEmitted) return [];
        _initEmitted = true;

        var data = node["data"];
        var model = data?["model"]?.GetValue<string>() ?? "copilot";

        var init = new JsonObject
        {
            ["type"] = "system",
            ["subtype"] = "init",
            ["session_id"] = "",
            ["model"] = model,
            ["tools"] = new JsonArray()
        };
        return [init.ToJsonString()];
    }

    private static IReadOnlyList<string> HandleAssistantMessage(JsonNode node)
    {
        var data = node["data"];
        if (data is null) return [];

        var content = data["content"]?.GetValue<string>() ?? "";
        var messageId = data["messageId"]?.GetValue<string>() ?? Guid.NewGuid().ToString();

        var results = new List<string>();

        if (!string.IsNullOrEmpty(content))
        {
            var textEvent = new JsonObject
            {
                ["type"] = "assistant",
                ["message"] = new JsonObject
                {
                    ["id"] = $"msg_{messageId}",
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = content
                        }
                    }
                }
            };
            results.Add(textEvent.ToJsonString());
        }

        var toolRequests = data["toolRequests"]?.AsArray();
        if (toolRequests is not null)
        {
            foreach (var request in toolRequests)
            {
                if (request is null) continue;

                var toolCallId = request["toolCallId"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
                var name = request["name"]?.GetValue<string>() ?? "unknown";

                // Skip meta-tools
                if (name is "report_intent") continue;

                JsonNode? args;
                var argsNode = request["arguments"];
                if (argsNode is JsonObject)
                    args = argsNode.DeepClone();
                else if (argsNode is not null)
                {
                    try { args = JsonNode.Parse(argsNode.GetValue<string>()); }
                    catch { args = new JsonObject(); }
                }
                else
                    args = new JsonObject();

                var toolUseEvent = new JsonObject
                {
                    ["type"] = "assistant",
                    ["message"] = new JsonObject
                    {
                        ["id"] = $"msg_tu_{toolCallId}",
                        ["type"] = "message",
                        ["role"] = "assistant",
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "tool_use",
                                ["id"] = toolCallId,
                                ["name"] = name,
                                ["input"] = args
                            }
                        }
                    }
                };
                results.Add(toolUseEvent.ToJsonString());
            }
        }

        return results;
    }

    private static IReadOnlyList<string> HandleToolExecutionStart(JsonNode node)
    {
        return [];
    }

    private static IReadOnlyList<string> HandleToolExecutionComplete(JsonNode node)
    {
        var data = node["data"];
        if (data is null) return [];

        var toolCallId = data["toolCallId"]?.GetValue<string>() ?? "";
        var result = data["result"];
        var content = result?["content"]?.GetValue<string>() ?? "";

        var toolResultEvent = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = toolCallId,
                        ["content"] = content
                    }
                }
            }
        };
        return [toolResultEvent.ToJsonString()];
    }

    private static IReadOnlyList<string> HandleResult(JsonNode node)
    {
        var usage = node["usage"];
        var totalApiMs = usage?["totalApiDurationMs"]?.GetValue<int>() ?? 0;
        var sessionMs = usage?["sessionDurationMs"]?.GetValue<int>() ?? 0;

        var resultEvent = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = "success",
            ["is_error"] = false,
            ["duration_ms"] = sessionMs > 0 ? sessionMs : totalApiMs,
            ["num_turns"] = 1,
            ["result"] = "",
            ["total_cost_usd"] = 0,
            ["usage"] = new JsonObject()
        };
        return [resultEvent.ToJsonString()];
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Helpers;

/// <summary>
/// Scans persisted agent event wire lines and synthesizes missing tool_result events for any
/// tool_call that lacks a matching result. Idempotent: running over already-reconciled lines
/// returns nothing.
/// </summary>
internal static class ToolStreamReconciler
{
    /// <summary>
    /// Builds one synthetic tool_result wire line per tool_call id that has no matching result.
    /// </summary>
    /// <param name="rawLines">The wire lines from the agent stream.</param>
    /// <param name="serializer">The event serializer used to produce wire format.</param>
    /// <param name="output">The output text to put in synthetic results.</param>
    /// <param name="isError">Whether to mark synthetic results as errors.</param>
    /// <param name="logger">Optional logger to record warnings when synthetic results are created.</param>
    /// <returns>A list of synthetic tool_result wire lines, in first-appearance order of the unclosed calls.</returns>
    public static IReadOnlyList<string> BuildMissingResultLines(
        IReadOnlyList<string> rawLines,
        IEventSerializer serializer,
        string output,
        bool isError,
        ILogger? logger = null)
    {
        if (rawLines == null || rawLines.Count == 0)
            return Array.Empty<string>();

        var toolCalls = new Dictionary<string, string>(StringComparer.Ordinal); // tool_use_id -> tool_name
        var toolResults = new HashSet<string>(StringComparer.Ordinal); // tool_use_id

        foreach (var line in rawLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("kind", out var kindProp))
                    continue;

                var kind = kindProp.GetString();
                if (kind == "tool_call")
                {
                    if (root.TryGetProperty("tool_use_id", out var idProp))
                    {
                        var toolUseId = idProp.GetString();
                        if (!string.IsNullOrEmpty(toolUseId))
                        {
                            var toolName = root.TryGetProperty("tool_name", out var nameProp)
                                ? nameProp.GetString() ?? "unknown"
                                : "unknown";
                            toolCalls[toolUseId] = toolName;
                        }
                    }
                }
                else if (kind == "tool_result")
                {
                    if (root.TryGetProperty("tool_use_id", out var idProp))
                    {
                        var toolUseId = idProp.GetString();
                        if (!string.IsNullOrEmpty(toolUseId))
                        {
                            toolResults.Add(toolUseId);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed lines are ignored
            }
        }

        var missing = toolCalls.Where(kv => !toolResults.Contains(kv.Key)).ToList();
        if (missing.Count == 0)
            return Array.Empty<string>();

        var syntheticLines = new List<string>(missing.Count);
        foreach (var (toolUseId, toolName) in missing)
        {
            logger?.LogWarning(
                "Tool call {ToolName} (id: {ToolUseId}) had no result — synthesizing one.",
                toolName,
                toolUseId);

            var syntheticResult = new ToolResultEvent
            {
                Kind = AgentEventKind.ToolResult,
                Timestamp = DateTimeOffset.UtcNow,
                ToolUseId = toolUseId,
                ToolName = toolName,
                Output = output,
                IsError = isError
            };

            var wireJson = serializer.Serialize(syntheticResult);
            if (!string.IsNullOrEmpty(wireJson))
            {
                syntheticLines.Add(wireJson);
            }
        }

        return syntheticLines;
    }
}

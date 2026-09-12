using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Helpers;
using Xunit;

namespace Ivy.Tendril.Test.Helpers;

public sealed class ToolStreamReconcilerTests
{
    private readonly JsonEventSerializer _serializer = new();

    [Fact]
    public void CallWithMatchingResult_YieldsNothing()
    {
        var toolCall = new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = "test-1",
            ToolName = "read_file",
            Timestamp = DateTimeOffset.UtcNow
        };
        var toolResult = new ToolResultEvent
        {
            Kind = AgentEventKind.ToolResult,
            ToolUseId = "test-1",
            Output = "file contents",
            Timestamp = DateTimeOffset.UtcNow
        };

        var lines = new List<string>
        {
            _serializer.Serialize(toolCall),
            _serializer.Serialize(toolResult)
        };

        var missing = ToolStreamReconciler.BuildMissingResultLines(
            lines,
            _serializer,
            "[No output]",
            isError: true);

        Assert.Empty(missing);
    }

    [Fact]
    public void CallWithNoResult_YieldsOneLine()
    {
        var toolCall = new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = "test-2",
            ToolName = "write_file",
            Timestamp = DateTimeOffset.UtcNow
        };

        var lines = new List<string>
        {
            _serializer.Serialize(toolCall)
        };

        var missing = ToolStreamReconciler.BuildMissingResultLines(
            lines,
            _serializer,
            "[No output]",
            isError: true);

        Assert.Single(missing);

        using var doc = JsonDocument.Parse(missing[0]);
        var root = doc.RootElement;

        Assert.Equal("tool_result", root.GetProperty("kind").GetString());
        Assert.Equal("test-2", root.GetProperty("tool_use_id").GetString());
        Assert.Equal("write_file", root.GetProperty("tool_name").GetString());
        Assert.Equal("[No output]", root.GetProperty("output").GetString());
        Assert.True(root.GetProperty("is_error").GetBoolean());
    }

    [Fact]
    public void TwoUnclosedCalls_YieldTwoLinesInOrder()
    {
        var call1 = new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = "test-3",
            ToolName = "tool_a",
            Timestamp = DateTimeOffset.UtcNow
        };
        var call2 = new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = "test-4",
            ToolName = "tool_b",
            Timestamp = DateTimeOffset.UtcNow
        };

        var lines = new List<string>
        {
            _serializer.Serialize(call1),
            _serializer.Serialize(call2)
        };

        var missing = ToolStreamReconciler.BuildMissingResultLines(
            lines,
            _serializer,
            "[Cancelled]",
            isError: true);

        Assert.Equal(2, missing.Count);

        using var doc1 = JsonDocument.Parse(missing[0]);
        Assert.Equal("test-3", doc1.RootElement.GetProperty("tool_use_id").GetString());
        Assert.Equal("tool_a", doc1.RootElement.GetProperty("tool_name").GetString());

        using var doc2 = JsonDocument.Parse(missing[1]);
        Assert.Equal("test-4", doc2.RootElement.GetProperty("tool_use_id").GetString());
        Assert.Equal("tool_b", doc2.RootElement.GetProperty("tool_name").GetString());
    }

    [Fact]
    public void Idempotent_RunningTwiceYieldsNothingOnSecondRun()
    {
        var toolCall = new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = "test-5",
            ToolName = "bash",
            Timestamp = DateTimeOffset.UtcNow
        };

        var lines = new List<string>
        {
            _serializer.Serialize(toolCall)
        };

        var firstRun = ToolStreamReconciler.BuildMissingResultLines(
            lines,
            _serializer,
            "[No output]",
            isError: true);

        Assert.Single(firstRun);

        // Add the synthetic result to the lines
        lines.AddRange(firstRun);

        var secondRun = ToolStreamReconciler.BuildMissingResultLines(
            lines,
            _serializer,
            "[No output]",
            isError: true);

        Assert.Empty(secondRun);
    }

    [Fact]
    public void MalformedAndNonToolLines_AreIgnored()
    {
        var toolCall = new ToolCallEvent
        {
            Kind = AgentEventKind.ToolCall,
            ToolUseId = "test-6",
            ToolName = "grep",
            Timestamp = DateTimeOffset.UtcNow
        };
        var textEvent = new TextEvent
        {
            Kind = AgentEventKind.Text,
            Text = "Some text",
            Timestamp = DateTimeOffset.UtcNow
        };

        var lines = new List<string>
        {
            "not json at all",
            _serializer.Serialize(toolCall),
            "{ malformed json",
            _serializer.Serialize(textEvent),
            ""
        };

        var missing = ToolStreamReconciler.BuildMissingResultLines(
            lines,
            _serializer,
            "[No output]",
            isError: true);

        Assert.Single(missing);

        using var doc = JsonDocument.Parse(missing[0]);
        Assert.Equal("test-6", doc.RootElement.GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public void EmptyInput_YieldsNothing()
    {
        var missing = ToolStreamReconciler.BuildMissingResultLines(
            Array.Empty<string>(),
            _serializer,
            "[No output]",
            isError: true);

        Assert.Empty(missing);
    }
}

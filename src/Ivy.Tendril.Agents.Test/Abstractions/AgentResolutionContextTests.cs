using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class AgentResolutionContextTests
{
    [Fact]
    public void MinimalContext_HasDefaults()
    {
        var ctx = new AgentResolutionContext
        {
            Prompt = "test",
            WorkingDirectory = "/tmp",
        };

        Assert.Null(ctx.AgentId);
        Assert.Equal(PermissionMode.FullAuto, ctx.PermissionMode);
        Assert.Empty(ctx.AllowedTools);
        Assert.Empty(ctx.DeniedTools);
        Assert.Empty(ctx.ExtraArguments);
        Assert.Empty(ctx.McpServers);
        Assert.Null(ctx.MaxTurns);
        Assert.Null(ctx.MaxBudgetUsd);
        Assert.Null(ctx.Metadata);
        Assert.Null(ctx.TimeoutPolicy);
        Assert.Null(ctx.InteractionHandler);
    }

    [Fact]
    public void FullContext_AllFieldsSet()
    {
        var ctx = new AgentResolutionContext
        {
            AgentId = AgentId.Claude,
            Prompt = "do the thing",
            WorkingDirectory = "/repo",
            Profile = "default",
            ModelOverride = "claude-opus-4-6",
            EffortOverride = EffortLevel.High,
            SessionId = "sess-abc",
            ExtraEnvironment = new Dictionary<string, string> { ["KEY"] = "val" },
            PromptFilePath = "/prompts/review.md",
            PreferredTransport = TransportKind.CliSpawn,
            Variables = new Dictionary<string, string> { ["branch"] = "main" },
            Metadata = new SessionMetadata { JobId = "j1" },
            TimeoutPolicy = TimeoutPolicy.Default,
            InteractionHandler = AutoApproveHandler.Instance,
            RecordingBasePath = "/logs",
            PermissionMode = PermissionMode.AcceptEdits,
            AllowedTools = ["Read", "Write"],
            DeniedTools = ["Bash"],
            ExtraArguments = ["--fast"],
            MaxTurns = 5,
            MaxBudgetUsd = 1.00m,
            McpServers = [new McpServerConfig("gh", "gh", ["mcp-server"])],
        };

        Assert.Equal(AgentId.Claude, ctx.AgentId);
        Assert.Equal(EffortLevel.High, ctx.EffortOverride);
        Assert.Equal(PermissionMode.AcceptEdits, ctx.PermissionMode);
        Assert.Equal(2, ctx.AllowedTools.Count);
        Assert.Single(ctx.DeniedTools);
        Assert.Equal(5, ctx.MaxTurns);
        Assert.Equal(1.00m, ctx.MaxBudgetUsd);
        Assert.Single(ctx.McpServers);
    }

    [Fact]
    public void McpServerConfig_CreatesCorrectly()
    {
        var mcp = new McpServerConfig(
            "github",
            "gh",
            ["mcp-server"],
            new Dictionary<string, string> { ["TOKEN"] = "abc" });

        Assert.Equal("github", mcp.Name);
        Assert.Equal("gh", mcp.Command);
        Assert.Single(mcp.Arguments);
        Assert.Equal("abc", mcp.Environment!["TOKEN"]);
    }

    [Fact]
    public void McpServerConfig_NullEnvironment_IsAllowed()
    {
        var mcp = new McpServerConfig("test", "cmd", []);
        Assert.Null(mcp.Environment);
    }
}

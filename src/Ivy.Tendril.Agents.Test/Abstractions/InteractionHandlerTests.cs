using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Agents.Test.Abstractions;

public class InteractionHandlerTests
{
    private static readonly InteractionContext TestContext = new()
    {
        SessionId = "test-session",
        AgentId = AgentId.Claude,
    };

    [Fact]
    public async Task AutoApproveHandler_Permission_GrantsAlways()
    {
        var request = new PermissionRequestEvent
        {
            Kind = AgentEventKind.PermissionRequest,
            RequestId = "req-1",
            ToolName = "Bash",
            Description = "Run command: ls",
        };

        var decision = await AutoApproveHandler.Instance.HandlePermissionAsync(request, TestContext);

        Assert.NotNull(decision);
        Assert.True(decision.Granted);
        Assert.Equal(PermissionScope.Session, decision.Scope);
        Assert.Equal(ResponseSource.Automation, decision.Source);
    }

    [Fact]
    public async Task AutoApproveHandler_Question_ReturnsNull()
    {
        var question = new UserQuestionEvent
        {
            Kind = AgentEventKind.UserQuestion,
            QuestionId = "q-1",
            Question = "Which option?",
        };

        var response = await AutoApproveHandler.Instance.HandleQuestionAsync(question, TestContext);

        Assert.Null(response);
    }

    [Fact]
    public async Task PassthroughHandler_Permission_ReturnsNull()
    {
        var request = new PermissionRequestEvent
        {
            Kind = AgentEventKind.PermissionRequest,
            RequestId = "req-1",
            ToolName = "Write",
        };

        var decision = await PassthroughHandler.Instance.HandlePermissionAsync(request, TestContext);

        Assert.Null(decision);
    }

    [Fact]
    public async Task PassthroughHandler_Question_ReturnsNull()
    {
        var question = new UserQuestionEvent
        {
            Kind = AgentEventKind.UserQuestion,
            QuestionId = "q-1",
            Question = "Pick one",
        };

        var response = await PassthroughHandler.Instance.HandleQuestionAsync(question, TestContext);

        Assert.Null(response);
    }

    [Fact]
    public void InteractionContext_SessionApprovedPatterns_DefaultsToEmpty()
    {
        var ctx = new InteractionContext
        {
            SessionId = "s1",
            AgentId = AgentId.Claude,
        };

        Assert.Empty(ctx.SessionApprovedPatterns);
    }

    [Fact]
    public void PermissionDecision_DefaultValues()
    {
        var decision = new PermissionDecision { Granted = false };
        Assert.Equal(PermissionScope.Once, decision.Scope);
        Assert.Equal(ResponseSource.Automation, decision.Source);
        Assert.Null(decision.UpdatedInput);
    }

    [Fact]
    public void QuestionResponse_WithAnswers()
    {
        var response = new QuestionResponse
        {
            Answers = ["option1", "option2"],
        };

        Assert.Equal(2, response.Answers!.Count);
        Assert.False(response.IsCancelled);
    }

    [Fact]
    public void QuestionResponse_Cancelled()
    {
        var response = new QuestionResponse { IsCancelled = true };
        Assert.True(response.IsCancelled);
    }
}

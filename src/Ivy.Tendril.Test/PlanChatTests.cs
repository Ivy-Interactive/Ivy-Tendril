using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;
using Xunit;

namespace Ivy.Tendril.Test;

public class PlanChatTests
{
    private static (ChatHistoryService Service, string TempDir) CreateChatService()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilPlanChatTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var configService = new ConfigService(new TendrilSettings(), tempDir);
        return (new ChatHistoryService(configService), tempDir);
    }

    private static PlanFile CreatePlan(int id, string title, PlanStatus status = PlanStatus.Draft, string? chatSessionId = null)
    {
        var folderName = $"{id:D5}-{title.Replace(' ', '-')}";
        var metadata = new PlanMetadata(
            Id: id,
            Project: "Acme",
            Level: "Feature",
            Title: title,
            State: status,
            Repos: [],
            Commits: [],
            Prs: [],
            Verifications: [],
            RelatedPlans: [],
            DependsOn: [],
            Created: DateTime.UtcNow,
            Updated: DateTime.UtcNow,
            InitialPrompt: null,
            SourceUrl: null,
            ChatSessionId: chatSessionId);

        return new PlanFile(metadata, "", $"/tmp/Plans/{folderName}", "");
    }

    [Fact]
    public void CreateSession_KeepsThePlanItBelongsToAcrossAReload()
    {
        var (service, tempDir) = CreateChatService();
        try
        {
            var session = service.CreateSession("claude", "opus", "#59 Revamp", planFolderName: "00059-revamp");
            Assert.Equal("00059-revamp", session.PlanFolderName);

            var reloaded = new ChatHistoryService(new ConfigService(new TendrilSettings(), tempDir)).GetSession(session.Id);
            Assert.NotNull(reloaded);
            Assert.Equal("00059-revamp", reloaded.PlanFolderName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindForPlan_PrefersThePlansOwnSessionOverTheGeneralChatThatCreatedIt()
    {
        var (service, tempDir) = CreateChatService();
        try
        {
            var general = service.CreateSession("claude", "opus", "Ideas");
            var plan = CreatePlan(59, "Revamp", chatSessionId: general.Id);

            Assert.Null(PlanChatSessions.FindForPlan(service, plan));

            var own = service.CreateSession("claude", "opus", "#59 Revamp", planFolderName: plan.FolderName);
            Assert.Equal(own.Id, PlanChatSessions.FindForPlan(service, plan)?.Id);

            var attached = CreatePlan(59, "Revamp", chatSessionId: own.Id);
            Assert.Equal(own.Id, PlanChatSessions.FindForPlan(service, attached)?.Id);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CreateForPlan_RecordsTheSessionOnAPlanThatHadNone()
    {
        var (service, tempDir) = CreateChatService();
        var planService = new FakePlanReaderService();
        try
        {
            var plan = CreatePlan(59, "Revamp");
            var session = PlanChatSessions.CreateForPlan(service, planService, plan, "claude", "opus", "high");

            Assert.Equal("#59 Revamp", session.Title);
            Assert.Equal(plan.FolderName, session.PlanFolderName);
            Assert.Equal("high", session.Effort);
            Assert.Equal((plan.FolderName, session.Id), planService.LastChatSessionAssignment);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CreateForPlan_LeavesAnExistingLinkToTheCreatingChatAlone()
    {
        var (service, tempDir) = CreateChatService();
        var planService = new FakePlanReaderService();
        try
        {
            var plan = CreatePlan(59, "Revamp", chatSessionId: "general");
            PlanChatSessions.CreateForPlan(service, planService, plan, "claude", "opus", null);

            Assert.Null(planService.LastChatSessionAssignment);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AttachedPlanSection_ScopesTheAgentToTheDraftAndItsJobs()
    {
        var plan = CreatePlan(59, "Revamp the User Authentication Experience");

        var section = ChatExecutionService.BuildAttachedPlanSection(plan, "sess-1");

        Assert.Contains("# Attached Plan", section);
        Assert.Contains("plan #59 \"Revamp the User Authentication Experience\"", section);
        Assert.Contains(plan.FolderPath, section);
        Assert.Contains("do not start jobs for any other plan", section);
        Assert.Contains($"tendril job start UpdatePlan {plan.FolderName} --instructions", section);
        Assert.Contains($"tendril job start ExecutePlan {plan.FolderName} --chat-session sess-1", section);
        Assert.DoesNotContain("CreatePr", section);
    }

    [Fact]
    public void AttachedPlanSection_OffersRetryAndPullRequestForAPlanUnderReview()
    {
        var plan = CreatePlan(60, "Night mode", PlanStatus.Review);

        var section = ChatExecutionService.BuildAttachedPlanSection(plan, "sess-2");

        Assert.Contains($"tendril job start RetryPlan {plan.FolderName} --change-request", section);
        Assert.Contains($"tendril job start CreatePr {plan.FolderName} --chat-session sess-2", section);
        Assert.DoesNotContain("UpdatePlan", section);
    }

    private sealed class RecordingChatExecutionService : IChatExecutionService
    {
        public List<(string SessionId, string Prompt, string? AgentId, string? ModelId)> Sent { get; } = [];
#pragma warning disable CS0067
        public event Action<string>? SessionGeneratingChanged;
        public event Action<string>? StreamUpdated;
#pragma warning restore CS0067
        public bool IsGenerating(string sessionId) => false;
        public string GetStreamSnapshot(string sessionId) => string.Empty;
        public IObservable<string> GetLiveStreamObservable(string sessionId) => System.Reactive.Linq.Observable.Empty<string>();

        public Task SendMessageAsync(string sessionId, string prompt, IReadOnlyList<ChatAttachmentDto>? attachments = null,
            string? agentId = null, string? modelId = null, string? effort = null, string role = "user", CancellationToken ct = default)
        {
            Sent.Add((sessionId, prompt, agentId, modelId));
            return Task.CompletedTask;
        }

        public Task CancelAsync(string sessionId) => Task.CompletedTask;
        public Task InterruptAsync(string sessionId) => Task.CompletedTask;

        public Task ForceSendMessageAsync(string sessionId, string prompt, IReadOnlyList<ChatAttachmentDto>? attachments = null,
            string? agentId = null, string? modelId = null, string? effort = null, CancellationToken ct = default) =>
            SendMessageAsync(sessionId, prompt, attachments, agentId, modelId, effort, ct: ct);

        public void Dispose() { }
    }

    [Fact]
    public void Send_StartsThePlanSessionOnFirstUseAndReusesItAfterwards()
    {
        var (service, tempDir) = CreateChatService();
        var planService = new FakePlanReaderService();
        var execution = new RecordingChatExecutionService();
        var config = new ConfigService(new TendrilSettings { CodingAgent = "codex" }, tempDir);
        var plan = CreatePlan(59, "Revamp");
        try
        {
            var first = PlanChatSessions.Send(service, execution, planService, TestAgentRunner.Create(), config, plan, "Let's talk");
            var second = PlanChatSessions.Send(service, execution, planService, TestAgentRunner.Create(), config, plan, "Again");

            Assert.Equal(first, second);
            Assert.Equal(plan.FolderName, service.GetSession(first)?.PlanFolderName);
            Assert.Equal((plan.FolderName, first), planService.LastChatSessionAssignment);
            Assert.Equal(2, execution.Sent.Count);
            Assert.Equal(("Let's talk", "codex"), (execution.Sent[0].Prompt, execution.Sent[0].AgentId));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DiscussPrompt_FollowsThePlansStage()
    {
        Assert.Contains("before executing it", PlanChatSessions.DiscussPrompt(CreatePlan(1, "Draft")));
        Assert.Contains("outcome of this plan", PlanChatSessions.DiscussPrompt(CreatePlan(2, "Done", PlanStatus.Review)));
    }

    [Fact]
    public void PlanTag_NamesThePlanASessionBelongsTo()
    {
        var now = DateTimeOffset.UtcNow;
        var attached = new ChatSessionModel("a", "#59 Revamp", now, now, "claude", "opus", [], PlanFolderName: "00059-revamp");
        var free = new ChatSessionModel("b", "Ideas", now, now, "claude", "opus", []);

        Assert.Equal("#59", ChatApp.PlanTag(attached));
        Assert.Null(ChatApp.PlanTag(free));
    }
}

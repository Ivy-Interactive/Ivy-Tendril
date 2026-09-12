using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Runtime;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.Logging.Abstractions;
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

            // A session becomes durable from its first message, plan-linked or not (see
            // ChatHistoryServiceTests.AddMessage_PersistsSessionToDisk). The reload below needs one
            // message so the session is persisted. What this asserts is that the plan link is part of
            // what gets persisted.
            service.AddMessage(session.Id, "user", "Let's talk");

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
    public void CreateSession_DoesNotPersistAPlanSessionUntilItHasAMessage()
    {
        var (service, tempDir) = CreateChatService();
        try
        {
            var session = service.CreateSession("claude", "opus", "#59 Revamp", planFolderName: "00059-revamp");
            Assert.Equal("00059-revamp", session.PlanFolderName);

            var chatsDir = Path.Combine(tempDir, "Chats");
            var sessionFile = Path.Combine(chatsDir, $"{session.Id}.json");
            Assert.False(File.Exists(sessionFile));

            var reloaded = new ChatHistoryService(new ConfigService(new TendrilSettings(), tempDir));
            Assert.Null(reloaded.GetSession(session.Id));

            service.AddMessage(session.Id, "user", "First message");

            Assert.True(File.Exists(sessionFile));
            var reloadedAfterMessage = new ChatHistoryService(new ConfigService(new TendrilSettings(), tempDir));
            Assert.NotNull(reloadedAfterMessage.GetSession(session.Id));
            Assert.Equal("00059-revamp", reloadedAfterMessage.GetSession(session.Id)?.PlanFolderName);
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
        public List<(string PlanFolderName, string Summary, string? Reason, string? SourceChatSessionId)> PlanEdits { get; } = [];
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

        public Task NotifyPlanEditAsync(string planFolderName, string summary, string? reason = null,
            string? sourceChatSessionId = null, string? revisionFile = null)
        {
            PlanEdits.Add((planFolderName, summary, reason, sourceChatSessionId));
            return Task.CompletedTask;
        }

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

    /// <summary>
    /// The side panel's session is found by the plan-edit fan-out only because
    /// <see cref="PlanChatSessions.CreateForPlan" /> stamps <c>PlanFolderName</c> on it. Nothing else
    /// links the two, so a session created without it would go quiet without failing anything.
    /// </summary>
    [Fact]
    public async Task PlanEditEvent_ReachesASessionCreatedForThePlan()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilPlanEditEventTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var configService = new ConfigService(new TendrilSettings { CodingAgent = "codex" }, tempDir);
            var chatService = new ChatHistoryService(configService);
            var planService = new FakePlanReaderService();
            var plan = CreatePlan(59, "Revamp");
            var agentRunner = TestAgentRunner.Create();

            using var execution = new ChatExecutionService(
                configService,
                chatService,
                agentRunner,
                new ChatSessionNamingService(agentRunner, configService, chatService,
                    NullLogger<ChatSessionNamingService>.Instance),
                new JsonEventSerializer());

            var session = PlanChatSessions.CreateForPlan(chatService, planService, plan, "codex", "gpt-5.6-sol", null);
            Assert.Equal(plan.FolderName, session.PlanFolderName);

            await execution.NotifyPlanEditAsync(plan.FolderName, "Solution changed (+3/-1 lines)",
                reason: "narrowed the scope");

            var deadline = DateTime.UtcNow.AddSeconds(20);
            ChatMessageModel? edit = null;
            while (DateTime.UtcNow < deadline && edit == null)
            {
                edit = chatService.GetSession(session.Id)?.Messages
                    .FirstOrDefault(m => m.Role == "system" && m.Content.Contains("was edited directly"));
                if (edit == null) await Task.Delay(25);
            }

            Assert.NotNull(edit);
            Assert.Contains("Solution changed (+3/-1 lines)", edit.Content);
            Assert.Contains("narrowed the scope", edit.Content);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
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

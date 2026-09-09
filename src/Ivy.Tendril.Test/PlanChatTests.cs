using System;
using System.IO;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
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

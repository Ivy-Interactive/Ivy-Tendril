using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

/// <summary>
///     Progressing a plan — executing the draft or opening its PR — retires the side chat that
///     belongs to that plan (#2530).
/// </summary>
public class JobServicePlanChatCleanupTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "PlanChatCleanupTest_" + Guid.NewGuid().ToString("N"));

    public JobServicePlanChatCleanupTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private sealed record Harness(
        JobService JobService,
        ChatHistoryService ChatService,
        string PlanFolder,
        string FolderName);

    private Harness CreateHarness()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        const string folderName = "00099-TestPlan";
        var planFolder = Path.Combine(_tempDir, folderName);
        Directory.CreateDirectory(planFolder);

        var configService = new ConfigService(new TendrilSettings(), _tempDir);
        var chatService = new ChatHistoryService(configService);

        var planReader = new FakePlanReaderService
        {
            PlanToReturn = new PlanFile(
                new PlanMetadata(
                    Id: 99,
                    Project: "TestProject",
                    Level: "NiceToHave",
                    Title: "Test",
                    State: PlanStatus.Draft,
                    Repos: [],
                    Commits: [],
                    Prs: [],
                    Verifications: [],
                    RelatedPlans: [],
                    DependsOn: [],
                    Created: DateTime.UtcNow,
                    Updated: DateTime.UtcNow,
                    InitialPrompt: null,
                    SourceUrl: null),
                LatestRevisionContent: string.Empty,
                FolderPath: planFolder,
                PlanYamlRaw: string.Empty)
        };

        var jobService = new JobService(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10),
            inboxPath: null,
            maxConcurrentJobs: 0,
            planReaderService: planReader,
            agentRunner: TestAgentRunner.Create(),
            chatHistoryService: chatService);

        return new Harness(jobService, chatService, planFolder, folderName);
    }

    private static ChatSessionModel AttachSession(ChatHistoryService chat, string folderName) =>
        chat.CreateSession("claude", "opus", title: "Plan chat", planFolderName: folderName);

    [Fact]
    public void StartJob_ExecutePlan_DeletesThePlansChatSession()
    {
        var h = CreateHarness();
        var session = AttachSession(h.ChatService, h.FolderName);

        h.JobService.StartJob(new ExecutePlanArgs(h.PlanFolder));

        Assert.Null(h.ChatService.GetSession(session.Id));
    }

    [Fact]
    public void StartJob_CreatePr_DeletesThePlansChatSession()
    {
        var h = CreateHarness();
        var session = AttachSession(h.ChatService, h.FolderName);

        h.JobService.StartJob(new CreatePrArgs(h.PlanFolder));

        Assert.Null(h.ChatService.GetSession(session.Id));
    }

    [Fact]
    public void StartJob_ExecutePlan_LeavesChatsOfOtherPlansAlone()
    {
        var h = CreateHarness();
        var other = AttachSession(h.ChatService, "00100-OtherPlan");

        h.JobService.StartJob(new ExecutePlanArgs(h.PlanFolder));

        Assert.NotNull(h.ChatService.GetSession(other.Id));
    }

    [Fact]
    public void StartJob_ExecutePlan_LeavesTheGeneralChatAlone()
    {
        var h = CreateHarness();
        /* The plan's chatSessionId may still point at the general chat that created it; that
           conversation is not about this plan and must survive. */
        var general = h.ChatService.CreateSession("claude", "opus", title: "General");

        h.JobService.StartJob(new ExecutePlanArgs(h.PlanFolder));

        Assert.NotNull(h.ChatService.GetSession(general.Id));
    }

    [Fact]
    public void StartJob_UpdatePlan_KeepsThePlansChatSession()
    {
        var h = CreateHarness();
        var session = AttachSession(h.ChatService, h.FolderName);

        /* Updating is not a progression: the conversation continues against the revised plan. */
        h.JobService.StartJob(new UpdatePlanArgs(h.PlanFolder, "tweak it"));

        Assert.NotNull(h.ChatService.GetSession(session.Id));
    }
}

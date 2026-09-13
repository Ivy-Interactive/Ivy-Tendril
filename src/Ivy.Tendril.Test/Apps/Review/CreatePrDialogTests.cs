using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core;
using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Review.Dialogs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Review;

public class CreatePrDialogTests
{
    private sealed class StubJobService : IJobService
    {
        public List<JobItem> Jobs { get; } = [];

        public string StartJob(JobArgsBase args, string? inboxFilePath = null)
        {
            var job = new JobItem
            {
                Id = "job-001",
                Type = args.Type,
                ChatSessionId = args.ChatSessionId,
                TypedArgs = args
            };
            Jobs.Add(job);
            return job.Id;
        }

        public void ForceStartJob(string id) { }
        public bool IsInboxFileTracked(string filePath) => false;
        public void CompleteJob(string id, int? exitCode, bool timedOut = false, bool staleOutput = false) { }
        public void StopJob(string id) { }
        public int StopAllJobs() => 0;
        public void DeleteJob(string id) { }
        public void ClearCompletedJobs() { }
        public void ClearFailedJobs() { }
        public void ClearAllJobs() { }
        public int StopQueuedJobs() => 0;
        public List<JobItem> GetJobs() => Jobs;
        public List<JobItem> GetJobsForPlan(string planFile) => [];
        public JobItem? GetJob(string id) => Jobs.FirstOrDefault(j => j.Id == id);
        public bool UpdateJobStatus(string id, string message, string? planId = null, string? planTitle = null) => false;
        public bool ReportJobFailure(string id, string message) => false;
        public void SetChatSessionId(string id, string chatSessionId) { }
#pragma warning disable CS0067
        public event Action? JobsChanged;
        public event Action? JobsStructureChanged;
        public event Action? JobPropertyChanged;
        public event Action<JobNotification>? NotificationReady;
        public event Action<JobItem>? JobFinished;
#pragma warning restore CS0067
        public void Dispose() { }
    }

    private sealed class StubGithubService : IGithubService
    {
        public List<RepoConfig> GetRepos() => [];
        public RepoConfig? GetRepoConfigFromPathCached(string repoPath) => null;
        public ProjectConfig? FindProjectForGithubRepo(string ownerRepo) => null;
        public IReadOnlyList<string> GetResolvedGithubRepos(ProjectConfig project) => [];
        public Task<(List<string> assignees, string? error)> GetAssigneesAsync(string owner, string repo) =>
            Task.FromResult((new List<string>(), (string?)null));
        public Task<(List<string> labels, string? error)> GetLabelsAsync(string owner, string repo) =>
            Task.FromResult((new List<string>(), (string?)null));
        public Task<(Dictionary<string, PrInfo> statuses, string? error)> GetPrStatusesAsync(string owner, string repo) =>
            Task.FromResult((new Dictionary<string, PrInfo>(), (string?)null));
        public Task<(List<GitHubIssue> issues, string? error)> SearchIssuesAsync(IssueSearchRequest request) =>
            Task.FromResult((new List<GitHubIssue>(), (string?)null));
        public Task<(List<GitHubIssue> issues, string? error)> GetMyAssignedIssuesAsync() =>
            Task.FromResult((new List<GitHubIssue>(), (string?)null));
        public Task<(List<GitHubReviewItem> prs, string? error)> GetReviewRequestsAsync() =>
            Task.FromResult((new List<GitHubReviewItem>(), (string?)null));
    }

    [Fact]
    public async Task CreatePrDialog_PropagatesChatSessionId_ToCreatePrArgs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CreatePrDialogPropagatesTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new TendrilSettings { CodingAgent = "codex" };
            var configService = new ConfigService(config, tempDir);
            var chatService = new ChatHistoryService(configService);
            var session = chatService.CreateSession("codex", "gpt-5.6-sol");

            var plan = new PlanFile(
                new PlanMetadata(92, "Tendril", "NiceToHave", "Create PR Dialog Plan", PlanStatus.Review,
                    [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null, ChatSessionId: session.Id),
                "# Create PR Dialog Plan",
                Path.Combine(tempDir, "00092-CreatePrDialogPlan"),
                "state: Review"
            );

            var dialogOpen = new State<bool>(true);
            var fakeJobService = new StubJobService();
            var stubGithub = new StubGithubService();
            var stubGit = new GitTabDataBuilderTests.StubGitService();

            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<IGitService>(stubGit);
            services.AddSingleton<IQueryService>(new QueryService(Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryService>.Instance));
            var appContext = (Ivy.AppContext)Activator.CreateInstance(
                typeof(Ivy.AppContext),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new object?[] { "conn1", "mach1", "review", "review", null, "http", "localhost", null },
                null)!;
            services.AddSingleton(appContext);
            var sp = services.BuildServiceProvider();

            var dialog = new CreatePrDialog(
                dialogOpen,
                plan,
                fakeJobService,
                refreshPlans: () => { },
                configService,
                stubGithub,
                gitService: stubGit,
                chatExecution: null,
                chatHistory: chatService);

            var ctx = new Ivy.Core.Hooks.ViewContext(() => { }, null, sp);
            dialog.BeforeBuild(ctx);
            var built = dialog.Build();
            dialog.AfterBuild();
            Assert.NotNull(built);
            var dialogWidget = Assert.IsType<Dialog>(built);

            var footer = dialogWidget.Children.OfType<DialogFooter>().FirstOrDefault();
            Assert.NotNull(footer);

            var createPrBtn = footer.Children.OfType<Button>().FirstOrDefault(b => b.Title == "Create PR");
            Assert.NotNull(createPrBtn);
            Assert.NotNull(createPrBtn.OnClick);

            await createPrBtn.OnClick.Invoke(new Event<Button>("click", createPrBtn));

            Assert.Single(fakeJobService.Jobs);
            var job = fakeJobService.Jobs[0];
            Assert.Equal(session.Id, job.ChatSessionId);
            var prArgs = Assert.IsType<CreatePrArgs>(job.TypedArgs);
            Assert.Equal(session.Id, prArgs.ChatSessionId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void BuildTargetBranchField_RendersSearchableSelect_WithProjectConfiguredBranchSelected()
    {
        var selectedBranch = new State<string>("development");
        var isCustomBranch = new State<bool>(false);
        var customBranchText = new State<string>("");
        var branches = new[] { "development", "feature/foo", "main" };

        var fieldObj = CreatePrDialog.BuildTargetBranchField(
            selectedBranch,
            isCustomBranch,
            customBranchText,
            branches,
            "development");

        Assert.NotNull(fieldObj);
        var field = Assert.IsType<Field>(fieldObj);
        Assert.Equal("Target Branch", field.Label);

        var select = Assert.IsType<SelectInput<string>>(Assert.Single(field.Children));
        Assert.True(select.Searchable);
        Assert.Equal("development", select.Value);
    }

    [Fact]
    public void BuildTargetBranchField_CustomBranchToggle_RendersTextInput()
    {
        var selectedBranch = new State<string>("development");
        var isCustomBranch = new State<bool>(true);
        var customBranchText = new State<string>("custom-epic-branch");
        var branches = new[] { "development", "feature/foo", "main" };

        var layoutObj = CreatePrDialog.BuildTargetBranchField(
            selectedBranch,
            isCustomBranch,
            customBranchText,
            branches,
            "development");

        Assert.NotNull(layoutObj);
        var layout = Assert.IsType<LayoutView>(layoutObj);

        var elementsField = typeof(LayoutView).GetField("_elements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var field = ((System.Collections.IEnumerable)elementsField?.GetValue(layout)!)
            .Cast<object>()
            .Select(el => el.GetType().GetProperty("Content")?.GetValue(el))
            .OfType<Field>()
            .FirstOrDefault();

        Assert.NotNull(field);
        var textInput = Assert.IsType<TextInput<string>>(Assert.Single(field.Children));
        Assert.Equal("custom-epic-branch", textInput.Value);
    }

    [Fact]
    public void BuildReviewersField_RendersSearchableSelect()
    {
        var reviewers = new State<string[]>(Array.Empty<string>());
        var assignees = new[] { "alice", "bob", "charlie" };

        var fieldObj = CreatePrDialog.BuildReviewersField(reviewers, assignees);

        Assert.NotNull(fieldObj);
        var field = Assert.IsType<Field>(fieldObj);
        Assert.Equal("Reviewers", field.Label);

        var select = Assert.IsType<SelectInput<string[]>>(Assert.Single(field.Children));
        Assert.True(select.Searchable);
    }
}


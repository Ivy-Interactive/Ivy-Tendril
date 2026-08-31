using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test;

public class JobsAppPromptDisplayTests
{
    [Fact]
    public void FlattenMarkdownLinks_ReplacesLinkWithText()
    {
        var result = JobsApp.FlattenMarkdownLinks("[GitHub Issue #925](https://github.com/o/r/issues/925)");

        Assert.Equal("GitHub Issue #925", result);
    }

    [Fact]
    public void FlattenMarkdownLinks_HandlesMultipleLinksAndPlainText()
    {
        var result = JobsApp.FlattenMarkdownLinks(
            "See [Issue #1](https://example.com/1) and also [Issue #2](https://example.com/2) for details.");

        Assert.Equal("See Issue #1 and also Issue #2 for details.", result);
    }

    [Fact]
    public void FlattenMarkdownLinks_LeavesUnmatchedBracketsAlone()
    {
        Assert.Equal("[not a link", JobsApp.FlattenMarkdownLinks("[not a link"));
        Assert.Equal("array[0] value", JobsApp.FlattenMarkdownLinks("array[0] value"));
    }

    [Fact]
    public void GetPromptDisplay_CreatePlanArgs_FlattensLink()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.CreatePlan,
            PlanFile = "",
            TypedArgs = new CreatePlanArgs(
                "[GitHub Issue #925](https://github.com/o/r/issues/925)", "Ivy-Tendril")
        };

        var display = JobsApp.GetPromptDisplay(job, new FakePlanReaderService());

        Assert.Equal("GitHub Issue #925", display);
    }

    [Fact]
    public void GetFullPrompt_PreservesRawMarkdown()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.CreatePlan,
            TypedArgs = new CreatePlanArgs(
                "[GitHub Issue #925](https://github.com/o/r/issues/925)", "Ivy-Tendril")
        };

        var fullPrompt = JobsApp.GetFullPrompt(job);

        Assert.Equal("[GitHub Issue #925](https://github.com/o/r/issues/925)", fullPrompt);
    }

    [Fact]
    public void GetFullPrompt_RetryPlanArgs_ReturnsChangeRequest()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.RetryPlan,
            TypedArgs = new RetryPlanArgs("00001-TestPlan", "Fix the failing tests")
        };

        var fullPrompt = JobsApp.GetFullPrompt(job);

        Assert.Equal("Fix the failing tests", fullPrompt);
    }

    [Fact]
    public void GetFullPrompt_UpdatePlanArgs_ReturnsInstructions()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.UpdatePlan,
            TypedArgs = new UpdatePlanArgs("00001-TestPlan", "Add error handling to service")
        };

        var fullPrompt = JobsApp.GetFullPrompt(job);

        Assert.Equal("Add error handling to service", fullPrompt);
    }

    [Fact]
    public void GetFullPrompt_ExecutePlanArgs_WithNote_ReturnsNote()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.ExecutePlan,
            TypedArgs = new ExecutePlanArgs("00001-TestPlan", "Special execution note")
        };

        var fullPrompt = JobsApp.GetFullPrompt(job);

        Assert.Equal("Special execution note", fullPrompt);
    }

    [Fact]
    public void GetFullPrompt_ExecutePlanArgs_WithoutNote_ResolvesFromPlan()
    {
        var plan = CreateSamplePlan("00001-TestPlan", "Sample Title", "Initial plan prompt");
        var planService = new FakePlanReaderService { PlanToReturn = plan };
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.ExecutePlan,
            PlanFile = "00001-TestPlan",
            TypedArgs = new ExecutePlanArgs("00001-TestPlan")
        };

        var fullPrompt = JobsApp.GetFullPrompt(job, planService);

        Assert.Equal("Initial plan prompt", fullPrompt);
    }

    [Fact]
    public void GetFullPrompt_AddProjectArgs_ReturnsProjectName()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.AddProject,
            TypedArgs = new AddProjectArgs("MyProject", [])
        };

        var fullPrompt = JobsApp.GetFullPrompt(job);

        Assert.Equal("MyProject", fullPrompt);
    }

    [Fact]
    public void GetFullPrompt_SetupProjectArgs_ReturnsFolderPath()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.SetupProject,
            TypedArgs = new SetupProjectArgs("/path/to/project")
        };

        var fullPrompt = JobsApp.GetFullPrompt(job);

        Assert.Equal("/path/to/project", fullPrompt);
    }

    [Fact]
    public void GetFullPrompt_SyncRepoArgs_ReturnsRepoPath()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.SyncRepo,
            TypedArgs = new SyncRepoArgs("/path/to/repo", "main")
        };

        var fullPrompt = JobsApp.GetFullPrompt(job);

        Assert.Equal("/path/to/repo", fullPrompt);
    }

    [Fact]
    public void GetFullPrompt_CreatePrArgs_WithComment_ReturnsComment()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.CreatePr,
            TypedArgs = new CreatePrArgs("00001-TestPlan", Comment: "Please review PR promptly")
        };

        var fullPrompt = JobsApp.GetFullPrompt(job);

        Assert.Equal("Please review PR promptly", fullPrompt);
    }

    [Fact]
    public void GetFullPrompt_CreateIssueArgs_WithComment_ReturnsComment()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.CreateIssue,
            TypedArgs = new CreateIssueArgs("00001-TestPlan", "owner/repo", Comment: "Issue comment details")
        };

        var fullPrompt = JobsApp.GetFullPrompt(job);

        Assert.Equal("Issue comment details", fullPrompt);
    }

    [Fact]
    public void GetPromptDisplay_AddProjectArgs_ReturnsProjectName()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.AddProject,
            TypedArgs = new AddProjectArgs("MyProject", [])
        };

        var display = JobsApp.GetPromptDisplay(job, new FakePlanReaderService());

        Assert.Equal("MyProject", display);
    }

    [Fact]
    public void GetPromptDisplay_SetupProjectArgs_ReturnsFolderPath()
    {
        var job = new JobItem
        {
            Id = "job-1",
            Type = Constants.JobTypes.SetupProject,
            TypedArgs = new SetupProjectArgs("/path/to/project")
        };

        var display = JobsApp.GetPromptDisplay(job, new FakePlanReaderService());

        Assert.Equal("/path/to/project", display);
    }

    private static PlanFile CreateSamplePlan(string folderName, string title, string? initialPrompt)
    {
        var metadata = new PlanMetadata(
            Id: 1,
            Project: "TestProject",
            Level: "Feature",
            Title: title,
            State: PlanStatus.Draft,
            Repos: [],
            Commits: [],
            Prs: [],
            Verifications: [],
            RelatedPlans: [],
            DependsOn: [],
            Created: DateTime.UtcNow,
            Updated: DateTime.UtcNow,
            InitialPrompt: initialPrompt,
            SourceUrl: null
        );

        return new PlanFile(
            Metadata: metadata,
            LatestRevisionContent: "",
            FolderPath: $"/tmp/{folderName}",
            PlanYamlRaw: ""
        );
    }

    private class FakePlanReaderService : IPlanReaderService
    {
        public string PlansDirectory => "/tmp";
        public bool IsDatabaseReady => true;
        public PlanFile? PlanToReturn { get; set; }
#pragma warning disable CS0067
        public event Action? CountsInvalidated;
#pragma warning restore CS0067

        public void MigratePlans()
        {
        }

        public void RecoverStuckPlans()
        {
        }

        public List<PlanFile> GetPlans(PlanStatus? statusFilter = null)
        {
            return [];
        }

        public PlanFile? GetPlanByFolder(string folderPath)
        {
            return PlanToReturn;
        }

        public List<PlanFile> GetIceboxPlans()
        {
            return [];
        }

        public void TransitionState(string folderName, PlanStatus newState)
        {
        }

        public IReadOnlyList<string> GetFailedVerifications(string folderName) => [];
        public void CompleteWithPartialDelivery(string folderName) { }

        public void ResetToDraft(string folderName)
        {
        }

        public void ResetVerificationsForRetry(string folderName)
        {
        }

        public void SetVerificationStatus(string folderName, string name, VerificationStatus status)
        {
        }

        public void RevertRevision(string folderName)
        {
        }

        public void SaveRevision(string folderName, string content)
        {
        }

        public string ReadLatestRevision(string folderName)
        {
            return "";
        }

        public List<(int Number, string Content, DateTime Modified)> GetRevisions(string folderName)
        {
            return [];
        }

        public void DeletePlan(string folderName)
        {
        }

        public string ReadRawPlan(string folderName)
        {
            return "";
        }

        public void SavePlan(string folderName, string fullContent)
        {
        }

        public void UpdateLatestRevision(string folderName, string content)
        {
        }

        public DashboardModels GetDashboardData(string? projectFilter)
        {
            return new DashboardModels(0, 0, 0, 0, 0, 0, 0, [], []);
        }

        public DashboardActivityStats GetDashboardActivity(int monthsBack = 24)
        {
            return new DashboardActivityStats([], 0);
        }

        public List<(DateOnly Date, int Count)> GetCompletedPrsByDay(int days)
        {
            return [];
        }

        public decimal GetPlanTotalCost(string folderPath)
        {
            return 0;
        }

        public int GetPlanTotalTokens(string folderPath)
        {
            return 0;
        }

        public List<HourlyTokenBurn> GetHourlyTokenBurn(int days = 7, string? projectFilter = null)
        {
            return [];
        }

        public List<Recommendation> GetRecommendations()
        {
            return [];
        }

        public int GetPendingRecommendationsCount()
        {
            return 0;
        }

        public PlanReaderService.PlanCountSnapshot ComputePlanCounts()
        {
            return new PlanReaderService.PlanCountSnapshot(0, 0, 0, 0, 0, 0);
        }

        public void UpdateRecommendationState(string planFolderName, string recommendationTitle, string newState,
            string? declineReason = null)
        {
        }

        public void SyncPlanArtifacts(string planFolder)
        {
        }

        public void InvalidateCaches()
        {
        }

        public Task FlushPendingWritesAsync()
        {
            return Task.CompletedTask;
        }

        public List<RecommendationYaml> GetRecommendationsForPlan(string folderName)
        {
            return [];
        }

        public void AcceptRecommendationAndRetry(string folderName, string recommendationTitle)
        {
        }

        public void AcceptRecommendationsAndRetry(string folderName, IReadOnlyCollection<string> titles)
        {
        }
    }
}

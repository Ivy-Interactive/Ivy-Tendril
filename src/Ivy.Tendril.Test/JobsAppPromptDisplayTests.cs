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

    private class FakePlanReaderService : IPlanReaderService
    {
        public string PlansDirectory => "/tmp";
        public bool IsDatabaseReady => true;
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
            return null;
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

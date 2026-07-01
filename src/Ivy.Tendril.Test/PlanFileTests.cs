using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test;

public class PlanFileTests
{
    private static PlanFile CreatePlanWithSourceUrl(string? sourceUrl)
    {
        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Review,
            [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, sourceUrl, null);
        return new PlanFile(metadata, "", "D:/plans/00001-TestPlan", "");
    }

    [Fact]
    public void IsPullRequestSource_WithPullUrl_ReturnsTrue()
    {
        var plan = CreatePlanWithSourceUrl("https://github.com/owner/repo/pull/123");
        Assert.True(plan.IsPullRequestSource);
    }

    [Fact]
    public void IsPullRequestSource_WithIssueUrl_ReturnsFalse()
    {
        var plan = CreatePlanWithSourceUrl("https://github.com/owner/repo/issues/123");
        Assert.False(plan.IsPullRequestSource);
    }

    [Fact]
    public void IsPullRequestSource_WithNullSource_ReturnsFalse()
    {
        var plan = CreatePlanWithSourceUrl(null);
        Assert.False(plan.IsPullRequestSource);
    }
}

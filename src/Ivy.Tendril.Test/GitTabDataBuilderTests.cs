using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test;

public class GitTabDataBuilderTests
{
    private static PlanFile CreatePlan(string folderPath, List<string> commits, List<string> prs)
    {
        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Draft,
            [], commits, prs, [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        return new PlanFile(metadata, "", folderPath, "");
    }

    private static GitTabDataBuilder.WorktreeSection CreateWorktreeSection() =>
        new("Ivy-Tendril", "/tmp/worktree", "tendril/00001", "abc1234", false, []);

    [Fact]
    public void CountGitItems_WithNoWorktreesCommitsOrPrs_ReturnsZero()
    {
        var plan = CreatePlan("/tmp/plan", [], []);
        var data = new GitTabDataBuilder.GitTabData([], []);

        var count = GitTabDataBuilder.CountGitItems(data, plan);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountGitItems_WithCommitsOnly_CountsCommits()
    {
        var plan = CreatePlan("/tmp/plan", ["abc1234", "def5678"], []);
        var data = new GitTabDataBuilder.GitTabData([], []);

        var count = GitTabDataBuilder.CountGitItems(data, plan);

        Assert.Equal(2, count);
    }

    [Fact]
    public void CountGitItems_SumsWorktreesCommitsAndPrs()
    {
        var plan = CreatePlan("/tmp/plan", ["abc1234", "def5678"], ["https://github.com/owner/repo/pull/1"]);
        var data = new GitTabDataBuilder.GitTabData([CreateWorktreeSection()], []);

        var count = GitTabDataBuilder.CountGitItems(data, plan);

        Assert.Equal(4, count);
    }
}

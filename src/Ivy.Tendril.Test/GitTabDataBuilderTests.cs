using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Test;

public class GitTabDataBuilderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"GitTabDataBuilderTests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

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

    private const string MainWorktreeBranch = "development";
    private const string MainWorktreeHash = "1df3f14ecommitfullhash000000000000000";

    private (PlanFile Plan, string WorktreePath) CreateBaseTestPlan()
    {
        var planFolder = Path.Combine(_tempDir, "plan");
        var worktreePath = Path.Combine(planFolder, "Worktrees", "Ivy-Tendril");
        Directory.CreateDirectory(worktreePath);
        return (CreatePlan(planFolder, [], []), worktreePath);
    }

    private static List<WorktreeInfo> WorktreesFor(string worktreePath) =>
    [
        new WorktreeInfo(worktreePath, "tendril/00087-Test", "abcdef1234567890"),
        new WorktreeInfo("/tmp/main-worktree", MainWorktreeBranch, MainWorktreeHash)
    ];

    [Fact]
    public void BuildGitTabData_WithUpstream_UsesMergeBaseAsBase()
    {
        var (plan, worktreePath) = CreateBaseTestPlan();
        var gitService = new StubGitService
        {
            Worktrees = WorktreesFor(worktreePath),
            WorktreeBase = new WorktreeBaseInfo("origin/epic-draft-questions", "8ba0663ff315cdbfa1894f05f0a6d8620ad2c866")
        };

        var data = GitTabDataBuilder.BuildGitTabData([], plan, null!, gitService);

        var section = Assert.Single(data.WorktreeSections);
        Assert.Equal("epic-draft-questions", section.BaseBranch);
        Assert.Equal("8ba0663", section.BaseShortHash);
        Assert.NotEqual(MainWorktreeBranch, section.BaseBranch);
        Assert.NotEqual(MainWorktreeHash[..7], section.BaseShortHash);
    }

    [Fact]
    public void BuildGitTabData_WithoutUpstream_FallsBackToMainWorktreeBranch()
    {
        var (plan, worktreePath) = CreateBaseTestPlan();
        var gitService = new StubGitService
        {
            Worktrees = WorktreesFor(worktreePath),
            WorktreeBase = null
        };

        var data = GitTabDataBuilder.BuildGitTabData([], plan, null!, gitService);

        var section = Assert.Single(data.WorktreeSections);
        Assert.Equal(MainWorktreeBranch, section.BaseBranch);
        Assert.Equal(MainWorktreeHash[..7], section.BaseShortHash);
    }

    [Fact]
    public void BuildGitTabData_WhenBaseLookupFails_FallsBackToMainWorktreeBranch()
    {
        var (plan, worktreePath) = CreateBaseTestPlan();
        var gitService = new StubGitService
        {
            Worktrees = WorktreesFor(worktreePath),
            WorktreeBaseFails = true
        };

        var data = GitTabDataBuilder.BuildGitTabData([], plan, null!, gitService);

        var section = Assert.Single(data.WorktreeSections);
        Assert.Equal(MainWorktreeBranch, section.BaseBranch);
        Assert.Equal(MainWorktreeHash[..7], section.BaseShortHash);
        Assert.Equal("/tmp/main-worktree", section.ParentRepoPath);
    }

    [Fact]
    public void BuildGitTabData_StripsOriginPrefixFromBaseBranch()
    {
        var (plan, worktreePath) = CreateBaseTestPlan();
        var gitService = new StubGitService
        {
            Worktrees = WorktreesFor(worktreePath),
            WorktreeBase = new WorktreeBaseInfo("origin/development", "8ba0663ff315cdbfa1894f05f0a6d8620ad2c866")
        };

        var data = GitTabDataBuilder.BuildGitTabData([], plan, null!, gitService);
        Assert.Equal("development", Assert.Single(data.WorktreeSections).BaseBranch);

        gitService.WorktreeBase = new WorktreeBaseInfo("upstream/main", "8ba0663ff315cdbfa1894f05f0a6d8620ad2c866");
        var data2 = GitTabDataBuilder.BuildGitTabData([], plan, null!, gitService);
        Assert.Equal("upstream/main", Assert.Single(data2.WorktreeSections).BaseBranch);
    }

    private class StubGitService : IGitService
    {
        public List<WorktreeInfo> Worktrees { get; set; } = [];
        public WorktreeBaseInfo? WorktreeBase { get; set; }
        public bool WorktreeBaseFails { get; set; }

        public GitResult<string> GetCommitTitle(string repoPath, string commitHash) =>
            GitResult<string>.Failure(GitError.CommandFailed, "Not implemented in stub");

        public GitResult<string> GetCommitDiff(string repoPath, string commitHash) =>
            GitResult<string>.Failure(GitError.CommandFailed, "Not implemented in stub");

        public GitResult<List<(string Status, string FilePath)>> GetCommitFiles(string repoPath, string commitHash) =>
            GitResult<List<(string Status, string FilePath)>>.Failure(GitError.CommandFailed, "Not implemented in stub");

        public GitResult<int> GetCommitFileCount(string repoPath, string commitHash) =>
            GitResult<int>.Failure(GitError.CommandFailed, "Not implemented in stub");

        public GitResult<string> GetCombinedDiff(string repoPath, string firstCommit, string lastCommit) =>
            GitResult<string>.Failure(GitError.CommandFailed, "Not implemented in stub");

        public GitResult<List<(string Status, string FilePath)>> GetCombinedChangedFiles(string repoPath, string firstCommit, string lastCommit) =>
            GitResult<List<(string Status, string FilePath)>>.Failure(GitError.CommandFailed, "Not implemented in stub");

        public GitResult<List<WorktreeInfo>> GetWorktrees(string repoPath) =>
            GitResult<List<WorktreeInfo>>.Success(Worktrees);

        public GitResult<Dictionary<string, (string Title, int FileCount)>> GetCommitSummaries(string repoPath, IEnumerable<string> commitHashes) =>
            GitResult<Dictionary<string, (string Title, int FileCount)>>.Failure(GitError.CommandFailed, "Not implemented in stub");

        public GitResult<bool> HasUncommittedChanges(string repoPath) =>
            GitResult<bool>.Success(false);

        public GitResult<List<string>> GetReachableCommits(string repoPath, IEnumerable<string> candidateHashes) =>
            GitResult<List<string>>.Success([]);

        public GitResult<DirtyRepoResult> GetRepoDirtyState(string repoPath, string expectedBaseBranch) =>
            GitResult<DirtyRepoResult>.Failure(GitError.CommandFailed, "Not implemented in stub");

        public GitResult<WorktreeBaseInfo?> GetWorktreeBase(string repoPath) =>
            WorktreeBaseFails
                ? GitResult<WorktreeBaseInfo?>.Failure(GitError.CommandFailed, "Not implemented in stub")
                : GitResult<WorktreeBaseInfo?>.Success(WorktreeBase);
    }
}

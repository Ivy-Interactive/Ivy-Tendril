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

    private static PlanFile CreatePlan(string folderPath, List<string> commits, List<string> prs, List<string>? repos = null)
    {
        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Draft,
            repos ?? [], commits, prs, [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
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

    [Fact]
    public void BuildGitTabData_ReportsRefStatus_ForCommitsNoWorktreeAccountsFor()
    {
        var planFolder = Path.Combine(_tempDir, "plan-refstatus");
        Directory.CreateDirectory(planFolder);
        var plan = CreatePlan(planFolder, ["aaaaaaa1", "bbbbbbb2"], [], repos: ["/tmp/repo-a"]);

        var gitService = new StubGitService
        {
            RefStatusByRepo =
            {
                ["/tmp/repo-a"] = new Dictionary<string, CommitRefStatus>
                {
                    ["aaaaaaa1"] = CommitRefStatus.Reachable,
                    ["bbbbbbb2"] = CommitRefStatus.Unreachable
                }
            }
        };

        var data = GitTabDataBuilder.BuildGitTabData(CommitRows("aaaaaaa1", "bbbbbbb2"), plan, null!, gitService);

        Assert.Equal(2, data.UnassociatedCommitRows.Count);
        Assert.NotNull(data.UnassociatedCommitRefStatus);
        Assert.Equal(CommitRefStatus.Reachable, data.UnassociatedCommitRefStatus!["aaaaaaa1"]);
        Assert.Equal(CommitRefStatus.Unreachable, data.UnassociatedCommitRefStatus["bbbbbbb2"]);
    }

    [Fact]
    public void BuildGitTabData_RefStatus_KeepsTheAnswerFromTheRepoThatHasTheCommit()
    {
        // A commit lives in one repo of a multi-repo plan. The other repo has never heard of it, and
        // that must not be mistaken for the commit being gone.
        var planFolder = Path.Combine(_tempDir, "plan-multirepo");
        Directory.CreateDirectory(planFolder);
        var plan = CreatePlan(planFolder, ["aaaaaaa1"], [], repos: ["/tmp/repo-a", "/tmp/repo-b"]);

        var gitService = new StubGitService
        {
            RefStatusByRepo =
            {
                ["/tmp/repo-a"] = new Dictionary<string, CommitRefStatus> { ["aaaaaaa1"] = CommitRefStatus.Missing },
                ["/tmp/repo-b"] = new Dictionary<string, CommitRefStatus> { ["aaaaaaa1"] = CommitRefStatus.Unreachable }
            }
        };

        var data = GitTabDataBuilder.BuildGitTabData(CommitRows("aaaaaaa1"), plan, null!, gitService);

        Assert.Equal(CommitRefStatus.Unreachable, data.UnassociatedCommitRefStatus!["aaaaaaa1"]);
    }

    [Fact]
    public void BuildGitTabData_RefStatus_IsEmpty_WhenNoRepoCanAnswer()
    {
        // Nothing is claimed about a commit the plan's repos could not be queried for, so the Git tab
        // shows it exactly as it does today rather than badging it as at risk.
        var planFolder = Path.Combine(_tempDir, "plan-norepo");
        Directory.CreateDirectory(planFolder);
        var plan = CreatePlan(planFolder, ["aaaaaaa1"], [], repos: ["/tmp/repo-missing"]);

        var data = GitTabDataBuilder.BuildGitTabData(CommitRows("aaaaaaa1"), plan, null!, new StubGitService());

        Assert.NotNull(data.UnassociatedCommitRefStatus);
        Assert.Empty(data.UnassociatedCommitRefStatus!);
    }

    private static List<PlanContentHelpers.CommitRow> CommitRows(params string[] hashes) =>
        hashes.Select(h => new PlanContentHelpers.CommitRow(h, h[..7], $"Commit {h}", 1)).ToList();

    private class StubGitService : IGitService
    {
        public List<WorktreeInfo> Worktrees { get; set; } = [];
        public WorktreeBaseInfo? WorktreeBase { get; set; }
        public bool WorktreeBaseFails { get; set; }
        public Dictionary<string, Dictionary<string, CommitRefStatus>> RefStatusByRepo { get; } = new();

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

        public GitResult<Dictionary<string, CommitRefStatus>> GetCommitRefStatus(string repoPath, IEnumerable<string> commitHashes)
        {
            if (!RefStatusByRepo.TryGetValue(repoPath, out var known))
                return GitResult<Dictionary<string, CommitRefStatus>>.Failure(GitError.InvalidRepoPath, $"Unknown repo: {repoPath}");

            return GitResult<Dictionary<string, CommitRefStatus>>.Success(
                commitHashes.ToDictionary(h => h, h => known.TryGetValue(h, out var status) ? status : CommitRefStatus.Missing));
        }

        public GitResult<DirtyRepoResult> GetRepoDirtyState(string repoPath, string expectedBaseBranch) =>
            GitResult<DirtyRepoResult>.Failure(GitError.CommandFailed, "Not implemented in stub");

        public GitResult<WorktreeBaseInfo?> GetWorktreeBase(string repoPath) =>
            WorktreeBaseFails
                ? GitResult<WorktreeBaseInfo?>.Failure(GitError.CommandFailed, "Not implemented in stub")
                : GitResult<WorktreeBaseInfo?>.Success(WorktreeBase);

        public GitResult<List<string>> GetBranches(string repoPath) =>
            GitResult<List<string>>.Success([]);
    }
}

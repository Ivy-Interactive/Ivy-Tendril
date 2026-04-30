using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.TestHelpers;

namespace Ivy.Tendril.Test;

public class PlanContentHelpersTests
{
    [Fact]
    public void GetArtifacts_WithSubDirectories_ReturnsCategorizedFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        try
        {
            var planDir = Path.Combine(tempDir, "00001-TestPlan");
            var screenshotsDir = Path.Combine(planDir, "artifacts", "screenshots");
            Directory.CreateDirectory(screenshotsDir);
            File.WriteAllText(Path.Combine(screenshotsDir, "shot1.png"), "fake");
            File.WriteAllText(Path.Combine(screenshotsDir, "shot2.png"), "fake");

            var result = PlanContentHelpers.GetArtifacts(planDir);

            Assert.True(result.ContainsKey("screenshots"));
            Assert.Equal(2, result["screenshots"].Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetArtifacts_WithNoArtifacts_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var result = PlanContentHelpers.GetArtifacts(tempDir);

            Assert.Empty(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BuildCommitRows_ReturnsCorrectShortHash()
    {
        var gitService = new StubGitService("Test commit",
            [("M", "file.cs")]);
        var config = new StubConfigService();

        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Draft,
            ["/fake/repo"], ["abcdef1234567890"], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        var plan = new PlanFile(metadata, "", Path.GetTempPath(), "");

        var rows = PlanContentHelpers.BuildCommitRows(plan, config, gitService);

        Assert.Single(rows);
        Assert.Equal("abcdef1", rows[0].ShortHash);
        Assert.Equal("abcdef1234567890", rows[0].Hash);
        Assert.Equal("Test commit", rows[0].Title);
        Assert.Equal(1, rows[0].FileCount);
    }

    [Fact]
    public void BuildCommitRows_WithNullTitle_SetsEmptyTitleAndNullFileCount()
    {
        var gitService = new StubGitService();
        var config = new StubConfigService();

        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Draft,
            ["/fake/repo"], ["abcdef1234567890"], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        var plan = new PlanFile(metadata, "", Path.GetTempPath(), "");

        var rows = PlanContentHelpers.BuildCommitRows(plan, config, gitService);

        Assert.Single(rows);
        Assert.Equal("", rows[0].Title);
        Assert.Null(rows[0].FileCount);
    }

    [Fact]
    public void BuildCommitRows_WithEmptyFiles_SetsZeroFileCount()
    {
        var gitService = new StubGitService("Some commit", []);
        var config = new StubConfigService();

        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Draft,
            ["/fake/repo"], ["abcdef1234567890"], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        var plan = new PlanFile(metadata, "", Path.GetTempPath(), "");

        var rows = PlanContentHelpers.BuildCommitRows(plan, config, gitService);

        Assert.Single(rows);
        Assert.Equal("Some commit", rows[0].Title);
        Assert.Equal(0, rows[0].FileCount);
    }

    [Fact]
    public void BuildCommitWarningCallout_WithEmptyTitle_ReturnsWarning()
    {
        var rows = new List<PlanContentHelpers.CommitRow>
        {
            new("abc123", "abc123", "", 5)
        };

        var result = PlanContentHelpers.BuildCommitWarningCallout(rows);

        Assert.NotNull(result);
        Assert.IsType<Callout>(result);
    }

    [Fact]
    public void BuildCommitWarningCallout_WithZeroFileCount_ReturnsWarning()
    {
        var rows = new List<PlanContentHelpers.CommitRow>
        {
            new("abc123", "abc123", "Some commit", 0)
        };

        var result = PlanContentHelpers.BuildCommitWarningCallout(rows);

        Assert.NotNull(result);
        Assert.IsType<Callout>(result);
    }

    [Fact]
    public void BuildCommitWarningCallout_WithNullFileCount_ReturnsNull()
    {
        var rows = new List<PlanContentHelpers.CommitRow>
        {
            new("abc123", "abc123", "Some commit", null)
        };

        var result = PlanContentHelpers.BuildCommitWarningCallout(rows);

        Assert.Null(result);
    }

    [Fact]
    public void BuildCommitWarningCallout_AllHealthy_ReturnsNull()
    {
        var rows = new List<PlanContentHelpers.CommitRow>
        {
            new("abc123", "abc123", "First commit", 3),
            new("def456", "def456", "Second commit", 1)
        };

        var result = PlanContentHelpers.BuildCommitWarningCallout(rows);

        Assert.Null(result);
    }

    [Fact]
    public void GetAllChangesData_WithMultipleCommits_ReturnsCombinedData()
    {
        var gitService = new StubGitService(
            "Test commit",
            [("A", "new.cs"), ("M", "existing.cs")],
            combinedDiff: "diff --git a/new.cs b/new.cs\n+new content",
            combinedFiles: [("A", "new.cs"), ("M", "existing.cs")]
        );
        var config = new StubConfigService();

        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Draft,
            ["/fake/repo"], ["commit1", "commit2"], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        var plan = new PlanFile(metadata, "", Path.GetTempPath(), "");

        var result = PlanContentHelpers.GetAllChangesData(plan, config, gitService);

        Assert.NotNull(result);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.ModifiedCount);
        Assert.Equal(0, result.DeletedCount);
        Assert.Contains("new content", result.Diff!);
    }

    [Fact]
    public void GetAllChangesData_WithSingleCommit_UsesCommitDiff()
    {
        var gitService = new StubGitService(
            "Single commit",
            [("A", "file.cs")],
            "diff --git a/file.cs b/file.cs\n+added"
        );
        var config = new StubConfigService();

        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Draft,
            ["/fake/repo"], ["abc123"], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        var plan = new PlanFile(metadata, "", Path.GetTempPath(), "");

        var result = PlanContentHelpers.GetAllChangesData(plan, config, gitService);

        Assert.NotNull(result);
        Assert.Single(result.Files);
        Assert.Equal(1, result.AddedCount);
        Assert.Contains("added", result.Diff!);
    }

    [Fact]
    public void GetAllChangesData_WithEmptyCommits_ReturnsNull()
    {
        var gitService = new StubGitService("Test", []);
        var config = new StubConfigService();

        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Draft,
            ["/fake/repo"], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        var plan = new PlanFile(metadata, "", Path.GetTempPath(), "");

        var result = PlanContentHelpers.GetAllChangesData(plan, config, gitService);

        Assert.Null(result);
    }

    [Fact]
    public void GetAllChangesData_WithCommitsNotInRepo_ReturnsNull()
    {
        var gitService = new StubGitService();
        var config = new StubConfigService();

        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Draft,
            ["/fake/repo"], ["unknown1", "unknown2"], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        var plan = new PlanFile(metadata, "", Path.GetTempPath(), "");

        var result = PlanContentHelpers.GetAllChangesData(plan, config, gitService);

        Assert.Null(result);
    }

    [Fact]
    public void SplitDiffByFile_MultiFileDiff_SplitsCorrectly()
    {
        var diff = "diff --git a/src/Foo.cs b/src/Foo.cs\n"
                   + "index abc1234..def5678 100644\n"
                   + "--- a/src/Foo.cs\n"
                   + "+++ b/src/Foo.cs\n"
                   + "@@ -1,3 +1,4 @@\n"
                   + " using System;\n"
                   + "+using System.Linq;\n"
                   + " namespace Foo;\n"
                   + "diff --git a/src/Bar.cs b/src/Bar.cs\n"
                   + "new file mode 100644\n"
                   + "index 0000000..abc1234\n"
                   + "--- /dev/null\n"
                   + "+++ b/src/Bar.cs\n"
                   + "@@ -0,0 +1,2 @@\n"
                   + "+namespace Bar;\n"
                   + "+public class Bar { }\n"
                   + "diff --git a/src/Baz.cs b/src/Baz.cs\n"
                   + "deleted file mode 100644\n"
                   + "index abc1234..0000000\n"
                   + "--- a/src/Baz.cs\n"
                   + "+++ /dev/null\n"
                   + "@@ -1,2 +0,0 @@\n"
                   + "-namespace Baz;\n"
                   + "-public class Baz { }\n";

        var files = new List<(string Status, string FilePath)>
        {
            ("M", "src/Foo.cs"),
            ("A", "src/Bar.cs"),
            ("D", "src/Baz.cs")
        };

        var changesData = new PlanContentHelpers.AllChangesData(diff, files, 1, 1, 1);
        var result = PlanContentHelpers.SplitDiffByFile(changesData);

        Assert.Equal(3, result.Count);

        Assert.Equal("src/Foo.cs", result[0].FilePath);
        Assert.Equal("M", result[0].Status);
        Assert.Contains("using System.Linq", result[0].Diff);

        Assert.Equal("src/Bar.cs", result[1].FilePath);
        Assert.Equal("A", result[1].Status);
        Assert.Contains("namespace Bar", result[1].Diff);

        Assert.Equal("src/Baz.cs", result[2].FilePath);
        Assert.Equal("D", result[2].Status);
        Assert.Contains("namespace Baz", result[2].Diff);
    }

    [Fact]
    public void SplitDiffByFile_EmptyDiff_ReturnsEmptyList()
    {
        var files = new List<(string Status, string FilePath)>();
        var changesData = new PlanContentHelpers.AllChangesData("", files, 0, 0, 0);

        var result = PlanContentHelpers.SplitDiffByFile(changesData);

        Assert.Empty(result);
    }

    [Fact]
    public void SplitDiffByFile_NullDiff_ReturnsEmptyList()
    {
        var files = new List<(string Status, string FilePath)>();
        var changesData = new PlanContentHelpers.AllChangesData(null, files, 0, 0, 0);

        var result = PlanContentHelpers.SplitDiffByFile(changesData);

        Assert.Empty(result);
    }

    [Fact]
    public void SplitDiffByFile_SingleFileDiff_ReturnsSingleItem()
    {
        var diff = "diff --git a/README.md b/README.md\n"
                   + "index abc1234..def5678 100644\n"
                   + "--- a/README.md\n"
                   + "+++ b/README.md\n"
                   + "@@ -1,2 +1,3 @@\n"
                   + " # Project\n"
                   + "+New line added\n";

        var files = new List<(string Status, string FilePath)>
        {
            ("M", "README.md")
        };

        var changesData = new PlanContentHelpers.AllChangesData(diff, files, 0, 1, 0);
        var result = PlanContentHelpers.SplitDiffByFile(changesData);

        Assert.Single(result);
        Assert.Equal("README.md", result[0].FilePath);
        Assert.Equal("M", result[0].Status);
        Assert.Contains("New line added", result[0].Diff);
    }

    [Fact]
    public void SplitDiffByFile_UnknownFileStatus_DefaultsToModified()
    {
        var diff = "diff --git a/unknown.txt b/unknown.txt\n"
                   + "index abc1234..def5678 100644\n"
                   + "--- a/unknown.txt\n"
                   + "+++ b/unknown.txt\n"
                   + "@@ -1 +1 @@\n"
                   + "-old\n"
                   + "+new\n";

        // File not in the files list — should default to "M"
        var files = new List<(string Status, string FilePath)>();
        var changesData = new PlanContentHelpers.AllChangesData(diff, files, 0, 0, 0);

        var result = PlanContentHelpers.SplitDiffByFile(changesData);

        Assert.Single(result);
        Assert.Equal("unknown.txt", result[0].FilePath);
        Assert.Equal("M", result[0].Status);
    }

    private class StubGitService(
        string? commitTitle = null,
        List<(string Status, string FilePath)>? commitFiles = null,
        string? commitDiff = null,
        string? combinedDiff = null,
        List<(string Status, string FilePath)>? combinedFiles = null) : IGitService
    {
        public GitResult<string> GetCommitTitle(string repoPath, string commitHash)
        {
            return commitTitle != null
                ? GitResult<string>.Success(commitTitle)
                : GitResult<string>.Failure(GitError.CommandFailed, "Not found");
        }

        public GitResult<string> GetCommitDiff(string repoPath, string commitHash)
        {
            return commitDiff != null
                ? GitResult<string>.Success(commitDiff)
                : GitResult<string>.Failure(GitError.CommandFailed, "Not found");
        }

        public GitResult<List<(string Status, string FilePath)>> GetCommitFiles(string repoPath, string commitHash)
        {
            return commitFiles != null
                ? GitResult<List<(string Status, string FilePath)>>.Success(commitFiles)
                : GitResult<List<(string Status, string FilePath)>>.Failure(GitError.CommandFailed, "Not found");
        }

        public GitResult<int> GetCommitFileCount(string repoPath, string commitHash)
        {
            return commitFiles != null
                ? GitResult<int>.Success(commitFiles.Count)
                : GitResult<int>.Failure(GitError.CommandFailed, "Not found");
        }

        public GitResult<string> GetCombinedDiff(string repoPath, string firstCommit, string lastCommit)
        {
            return combinedDiff != null
                ? GitResult<string>.Success(combinedDiff)
                : GitResult<string>.Failure(GitError.CommandFailed, "Not found");
        }

        public GitResult<List<(string Status, string FilePath)>> GetCombinedChangedFiles(string repoPath, string firstCommit,
            string lastCommit)
        {
            return combinedFiles != null
                ? GitResult<List<(string Status, string FilePath)>>.Success(combinedFiles)
                : GitResult<List<(string Status, string FilePath)>>.Failure(GitError.CommandFailed, "Not found");
        }

        public GitResult<List<WorktreeInfo>> GetWorktrees(string repoPath)
        {
            return GitResult<List<WorktreeInfo>>.Failure(GitError.CommandFailed, "Not implemented in stub");
        }

        public GitResult<Dictionary<string, (string Title, int FileCount)>> GetCommitSummaries(string repoPath, IEnumerable<string> commitHashes)
        {
            if (commitTitle == null)
                return GitResult<Dictionary<string, (string Title, int FileCount)>>.Failure(GitError.CommandFailed, "Not found");

            var summaries = commitHashes.ToDictionary(h => h, _ => (commitTitle, commitFiles?.Count ?? 0));
            return GitResult<Dictionary<string, (string Title, int FileCount)>>.Success(summaries);
        }
    }
}
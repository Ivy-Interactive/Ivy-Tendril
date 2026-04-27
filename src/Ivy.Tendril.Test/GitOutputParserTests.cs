using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test;

public class GitOutputParserTests
{
    [Fact]
    public void ParseNameStatusOutput_WithValidInput_ReturnsCorrectFiles()
    {
        var output = "M\tsrc/file1.cs\nA\tsrc/file2.cs\nD\tsrc/file3.cs";

        var result = GitOutputParser.ParseNameStatusOutput(output);

        Assert.Equal(3, result.Count);
        Assert.Equal(("M", "src/file1.cs"), result[0]);
        Assert.Equal(("A", "src/file2.cs"), result[1]);
        Assert.Equal(("D", "src/file3.cs"), result[2]);
    }

    [Fact]
    public void ParseNameStatusOutput_WithEmptyInput_ReturnsEmptyList()
    {
        var output = "";

        var result = GitOutputParser.ParseNameStatusOutput(output);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseNameStatusOutput_WithInvalidLines_SkipsThem()
    {
        var output = "M\tsrc/file1.cs\nInvalidLine\nA\tsrc/file2.cs";

        var result = GitOutputParser.ParseNameStatusOutput(output);

        Assert.Equal(2, result.Count);
        Assert.Equal(("M", "src/file1.cs"), result[0]);
        Assert.Equal(("A", "src/file2.cs"), result[1]);
    }

    [Fact]
    public void ParseWorktreeList_WithValidInput_ReturnsCorrectWorktrees()
    {
        var output = @"worktree /path/to/main
HEAD abc123
branch refs/heads/main

worktree /path/to/feature
HEAD def456
branch refs/heads/feature/test

worktree /path/to/another
HEAD ghi789
branch refs/heads/another-branch
";

        var result = GitOutputParser.ParseWorktreeList(output);

        Assert.Equal(3, result.Count);
        Assert.Equal("/path/to/main", result[0].Path);
        Assert.Equal("main", result[0].Branch);
        Assert.Equal("abc123", result[0].Hash);

        Assert.Equal("/path/to/feature", result[1].Path);
        Assert.Equal("feature/test", result[1].Branch);
        Assert.Equal("def456", result[1].Hash);

        Assert.Equal("/path/to/another", result[2].Path);
        Assert.Equal("another-branch", result[2].Branch);
        Assert.Equal("ghi789", result[2].Hash);
    }

    [Fact]
    public void ParseWorktreeList_WithIncompleteWorktree_SkipsIt()
    {
        var output = @"worktree /path/to/main
HEAD abc123

worktree /path/to/feature
HEAD def456
branch refs/heads/feature
";

        var result = GitOutputParser.ParseWorktreeList(output);

        Assert.Single(result);
        Assert.Equal("/path/to/feature", result[0].Path);
        Assert.Equal("feature", result[0].Branch);
        Assert.Equal("def456", result[0].Hash);
    }

    [Fact]
    public void ParseCommitSummaries_WithValidInput_ReturnsCorrectSummaries()
    {
        var output = "abc123\0First commit title\n10\t5\tfile1.cs\n20\t10\tfile2.cs\n\ndef456\0Second commit title\n5\t3\tfile3.cs\n";
        var inputHashes = new HashSet<string> { "abc123", "def456" };

        var result = GitOutputParser.ParseCommitSummaries(output, inputHashes);

        Assert.Equal(2, result.Count);
        Assert.Equal("First commit title", result["abc123"].Title);
        Assert.Equal(2, result["abc123"].FileCount);
        Assert.Equal("Second commit title", result["def456"].Title);
        Assert.Equal(1, result["def456"].FileCount);
    }

    [Fact]
    public void ParseCommitSummaries_WithAbbreviatedHash_MapsToFullHash()
    {
        var output = "abc123def456\0Commit title\n10\t5\tfile1.cs\n";
        var inputHashes = new HashSet<string> { "abc123" };

        var result = GitOutputParser.ParseCommitSummaries(output, inputHashes);

        Assert.Equal(2, result.Count);
        Assert.Equal("Commit title", result["abc123def456"].Title);
        Assert.Equal("Commit title", result["abc123"].Title);
        Assert.Equal(1, result["abc123"].FileCount);
    }

    [Fact]
    public void ParseCommitSummaries_WithEmptyTitle_StoresEmptyString()
    {
        var output = "abc123\0\n10\t5\tfile1.cs\n";
        var inputHashes = new HashSet<string> { "abc123" };

        var result = GitOutputParser.ParseCommitSummaries(output, inputHashes);

        Assert.Single(result);
        Assert.Equal("", result["abc123"].Title);
        Assert.Equal(1, result["abc123"].FileCount);
    }
}

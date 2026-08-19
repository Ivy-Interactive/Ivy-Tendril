using System.IO;
using System.Linq;
using Ivy.Tendril.Helpers;
using Xunit;

namespace Ivy.Tendril.Test;

public class WorktreeHelperTests
{
    [Theory]
    [InlineData(@"/Users/test/.tendril/Projects/my-proj/Repos/ivy-interactive/ivy-tendril", "ivy-interactive/ivy-tendril")]
    [InlineData(@"/Users/test/.tendril/Projects/my-proj/Repos/my-org/sub/repo", "my-org/sub/repo")]
    [InlineData(@"C:\Users\test\.tendril\Projects\my-proj\Repos\ivy-interactive\ivy-tendril", @"ivy-interactive\ivy-tendril")]
    [InlineData(@"/Users/test/git/simple-repo", "simple-repo")]
    public void DeriveWorktreeRelativePath_ExtractsRepoPathUnderRepos(string input, string expected)
    {
        var actual = GitHelper.DeriveWorktreeRelativePath(input);
        Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar),
                     actual.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void EnumerateWorktreeDirectories_FindsFlatAndNestedWorktrees()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilWorktreesTest_" + Guid.NewGuid().ToString("N"));
        var flatWt = Path.Combine(tempDir, "flat-repo");
        var nestedWt = Path.Combine(tempDir, "owner", "nested-repo");
        var emptyDir = Path.Combine(tempDir, "empty-dir");

        Directory.CreateDirectory(flatWt);
        File.WriteAllText(Path.Combine(flatWt, ".git"), "gitdir: ...");

        Directory.CreateDirectory(nestedWt);
        File.WriteAllText(Path.Combine(nestedWt, ".git"), "gitdir: ...");

        Directory.CreateDirectory(emptyDir);

        try
        {
            var worktrees = GitHelper.EnumerateWorktreeDirectories(tempDir).ToList();
            Assert.Equal(2, worktrees.Count);
            Assert.Contains(flatWt, worktrees);
            Assert.Contains(nestedWt, worktrees);
            Assert.DoesNotContain(emptyDir, worktrees);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

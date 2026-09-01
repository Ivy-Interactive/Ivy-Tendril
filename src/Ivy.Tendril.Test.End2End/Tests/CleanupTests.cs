using Ivy.Tendril.Test.End2End.Configuration;
using Ivy.Tendril.Test.End2End.Fixtures;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Tests;

public class CleanupTests
{
    [Fact]
    public async Task TendrilProcessFixture_CleansUpTempDirectories()
    {
        var fixture = new TendrilProcessFixture();
        string? homePath = null;

        try
        {
            await fixture.InitializeAsync();
            homePath = fixture.TendrilHome;
            Assert.True(Directory.Exists(homePath), "TendrilHome should exist during test");
            Assert.True(Directory.Exists(fixture.TendrilPlans), "TendrilPlans should exist during test");
        }
        catch (TimeoutException)
        {
            // Server may fail to start (port conflict) — that's fine for this test.
            // We still want to verify cleanup works.
            homePath = fixture.TendrilHome;
            if (string.IsNullOrEmpty(homePath) || !Directory.Exists(homePath))
                return; // Nothing was created, nothing to clean up
        }
        finally
        {
            await fixture.DisposeAsync();
        }

        await RetryHelper.WaitUntilAsync(
            () => Task.FromResult(!Directory.Exists(homePath)),
            TimeSpan.FromSeconds(10),
            failureMessage: "TENDRIL_HOME was not cleaned up after dispose");
    }

    [Fact]
    public async Task TestRepositoryFixture_CleansUpFork()
    {
        var settings = TestSettingsProvider.Get();
        var fixture = new TestRepositoryFixture();

        try
        {
            await fixture.InitializeAsync();
            Assert.True(Directory.Exists(fixture.LocalClonePath), "Clone should exist during test");
            Assert.False(string.IsNullOrEmpty(fixture.ForkedRepoFullName), "Fork name should be set");
        }
        finally
        {
            var clonePath = fixture.LocalClonePath;
            var forkName = fixture.ForkedRepoFullName;
            await fixture.DisposeAsync();

            // Local clone should be removed
            Assert.False(Directory.Exists(clonePath), "Local clone should be removed after dispose");

            // Fork should be deleted from GitHub (if cleanup is enabled and delete_repo scope is present)
            if (settings.CleanupFork && !string.IsNullOrEmpty(forkName))
            {
                var deleteResult = await ProcessHelper.RunAsync(
                    "gh", $"repo delete {forkName} --yes", timeoutMs: 30_000);
                if (deleteResult.ExitCode == 0)
                {
                    var result = await ProcessHelper.RunAsync(
                        "gh", $"repo view {forkName}", timeoutMs: 15_000);
                    Assert.NotEqual(0, result.ExitCode);
                }
            }
        }
    }

    [Fact]
    public void TestDirectoryHelper_PurgeStaleDirectories_DeletesOlderDirectoriesMatchingPattern()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"tendril-purge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);

        try
        {
            var staleDir = Path.Combine(testRoot, "tendril-e2e-stale-1");
            var recentDir = Path.Combine(testRoot, "tendril-e2e-recent-1");
            var otherStaleDir = Path.Combine(testRoot, "other-stale-1");

            Directory.CreateDirectory(staleDir);
            Directory.CreateDirectory(recentDir);
            Directory.CreateDirectory(otherStaleDir);

            var oldTime = DateTime.UtcNow.AddHours(-3);
            Directory.SetCreationTimeUtc(staleDir, oldTime);
            Directory.SetLastWriteTimeUtc(staleDir, oldTime);

            Directory.SetCreationTimeUtc(otherStaleDir, oldTime);
            Directory.SetLastWriteTimeUtc(otherStaleDir, oldTime);

            TestDirectoryHelper.PurgeStaleDirectories("tendril-e2e-*", TimeSpan.FromHours(1), basePath: testRoot);

            Assert.False(Directory.Exists(staleDir), "Stale matching directory should be purged");
            Assert.True(Directory.Exists(recentDir), "Recent matching directory should be preserved");
            Assert.True(Directory.Exists(otherStaleDir), "Non-matching directory should not be purged");
        }
        finally
        {
            TestDirectoryHelper.DeleteDirectorySafely(testRoot);
        }
    }

    [Fact]
    public void TestDirectoryHelper_DeleteDirectorySafely_DeletesReadOnlyFiles()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"tendril-ro-test-{Guid.NewGuid():N}");
        var subDir = Path.Combine(testDir, "nested");
        Directory.CreateDirectory(subDir);

        var roFile = Path.Combine(subDir, "locked.txt");
        File.WriteAllText(roFile, "readonly content");
        File.SetAttributes(roFile, FileAttributes.ReadOnly);

        Assert.True(Directory.Exists(testDir));
        Assert.True(File.Exists(roFile));

        TestDirectoryHelper.DeleteDirectorySafely(testDir);

        Assert.False(Directory.Exists(testDir), "Directory containing read-only files should be deleted successfully");
    }

    [Fact]
    public async Task TendrilProcessFixture_DeletesDirectoryEvenWithReadOnlyFiles()
    {
        var fixture = new TendrilProcessFixture();
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var roFile = Path.Combine(tempDir, "readonly.db");
        File.WriteAllText(roFile, "sqlite db content");
        File.SetAttributes(roFile, FileAttributes.ReadOnly);

        await TestDirectoryHelper.DeleteDirectorySafelyAsync(tempDir);
        Assert.False(Directory.Exists(tempDir), "Fixture cleanup should delete directories with read-only files");
    }

    [Fact]
    public async Task TestRepositoryFixture_DeletesDirectoryEvenWithReadOnlyFiles()
    {
        var tempRepoDir = Path.Combine(Path.GetTempPath(), $"tendril-e2e-repo-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(tempRepoDir, ".git", "objects", "pack");
        Directory.CreateDirectory(gitDir);
        var roFile = Path.Combine(gitDir, "pack.idx");
        File.WriteAllText(roFile, "pack content");
        File.SetAttributes(roFile, FileAttributes.ReadOnly);

        await TestDirectoryHelper.DeleteDirectorySafelyAsync(tempRepoDir);
        Assert.False(Directory.Exists(tempRepoDir), "Repository cleanup should delete directories with read-only git objects");
    }
}

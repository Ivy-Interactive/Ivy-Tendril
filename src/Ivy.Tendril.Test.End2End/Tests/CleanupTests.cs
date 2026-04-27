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

        try
        {
            await fixture.InitializeAsync();
            Assert.True(Directory.Exists(fixture.TendrilHome), "TendrilHome should exist during test");
            Assert.True(Directory.Exists(fixture.TendrilPlans), "TendrilPlans should exist during test");
        }
        finally
        {
            var homePath = fixture.TendrilHome;
            await fixture.DisposeAsync();

            // After dispose, the directories should be cleaned up
            await RetryHelper.WaitUntilAsync(
                () => Task.FromResult(!Directory.Exists(homePath)),
                TimeSpan.FromSeconds(10),
                failureMessage: "TENDRIL_HOME was not cleaned up after dispose");
        }
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

            // Fork should be deleted from GitHub (if cleanup is enabled)
            if (settings.CleanupFork && !string.IsNullOrEmpty(forkName))
            {
                var result = await ProcessHelper.RunAsync(
                    "gh", $"repo view {forkName}", timeoutMs: 15_000);
                Assert.NotEqual(0, result.ExitCode);
            }
        }
    }
}

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

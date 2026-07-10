using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test;

public class PlanCommandHelpersReadPlanRaceTests : IDisposable
{
    private readonly string _planFolder;

    public PlanCommandHelpersReadPlanRaceTests()
    {
        _planFolder = Path.Combine(Path.GetTempPath(), $"tendril-readplan-race-{Guid.NewGuid()}");
        Directory.CreateDirectory(_planFolder);
        File.WriteAllText(Path.Combine(_planFolder, "plan.yaml"), PlanYaml("Test Plan"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_planFolder))
            Directory.Delete(_planFolder, true);
    }

    private static string PlanYaml(string title) =>
        $"state: Draft\nproject: Test\ntitle: {title}\nupdated: 2026-01-01T00:00:00Z\n";

    [Fact]
    public async Task ReadPlan_ConcurrentTruncatingWrites_NeverReturnsEmptyOrThrows()
    {
        var yamlPath = Path.Combine(_planFolder, "plan.yaml");
        using var stopSignal = new CancellationTokenSource();

        // Mirrors PlanReaderService.ExecuteWithLock: acquire the same PlanFileLock and rewrite
        // plan.yaml via the non-atomic truncate+write FileHelper.WriteAllText, on a background thread.
        var writer = Task.Run(() =>
        {
            while (!stopSignal.IsCancellationRequested)
            {
                using var _ = PlanFileLock.Acquire(_planFolder);
                FileHelper.WriteAllText(yamlPath, PlanYaml("Test Plan"));
            }
        });

        var deadline = DateTime.UtcNow.AddSeconds(1);
        var readCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            var plan = PlanCommandHelpers.ReadPlan(_planFolder);
            Assert.NotNull(plan);
            Assert.Equal("Test Plan", plan.Title);
            readCount++;
        }

        stopSignal.Cancel();
        await writer;

        Assert.True(readCount > 0);
    }
}

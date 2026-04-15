using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.TestHelpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Test.Services;

public class PlanReaderServiceTests
{
    private class TestPlanWatcherService : IPlanWatcherService
    {
        public List<string> NotifiedFolders { get; } = new();
        public event Action<string?>? PlansChanged;

        public void NotifyChanged(string? folderName = null)
        {
            if (folderName != null)
                NotifiedFolders.Add(folderName);
        }

        public void Dispose() { }
    }

    private class TestLogger : ILogger<PlanReaderService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    [Fact]
    public void TransitionState_NotifiesPlanWatcher()
    {
        // Arrange
        var testConfig = new StubConfigService();
        var testLogger = new TestLogger();
        var testWatcher = new TestPlanWatcherService();

        var service = new PlanReaderService(
            testConfig,
            testLogger,
            planWatcherService: testWatcher);

        var folderName = "01234-TestPlan";

        // Create a temporary plan folder and plan.yaml file
        var planFolder = Path.Combine(testConfig.PlanFolder, folderName);
        Directory.CreateDirectory(planFolder);
        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        File.WriteAllText(planYamlPath, "state: Draft\nproject: TestProject\n");

        try
        {
            // Act
            service.TransitionState(folderName, PlanStatus.ReadyForReview);

            // Assert
            Assert.Contains(folderName, testWatcher.NotifiedFolders);
            Assert.Single(testWatcher.NotifiedFolders);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(planFolder))
                Directory.Delete(planFolder, true);
        }
    }

    [Fact]
    public void SaveRevision_NotifiesPlanWatcher()
    {
        // Arrange
        var testConfig = new StubConfigService();
        var testLogger = new TestLogger();
        var testWatcher = new TestPlanWatcherService();

        var service = new PlanReaderService(
            testConfig,
            testLogger,
            planWatcherService: testWatcher);

        var folderName = "01234-TestPlan";
        var content = "# Test Revision\n\nTest content";

        // Create a temporary plan folder
        var planFolder = Path.Combine(testConfig.PlanFolder, folderName);
        Directory.CreateDirectory(planFolder);

        try
        {
            // Act
            service.SaveRevision(folderName, content);

            // Give background write a moment to complete
            Thread.Sleep(100);

            // Assert
            Assert.Contains(folderName, testWatcher.NotifiedFolders);
            Assert.Single(testWatcher.NotifiedFolders);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(planFolder))
                Directory.Delete(planFolder, true);
        }
    }
}

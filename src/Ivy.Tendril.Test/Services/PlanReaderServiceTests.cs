using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Test.TestHelpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Test.Services;

public class PlanReaderServiceTests
{
    [Fact]
    public async Task TransitionState_NotifiesPlanWatcher()
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
            service.TransitionState(folderName, PlanStatus.Review);

            // Wait for the background plan.yaml write so the finally-block cleanup doesn't race it.
            await service.FlushPendingWritesAsync();

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
    public async Task SaveRevision_NotifiesPlanWatcher()
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

            // Deterministically wait for the background write to finish (not a fixed sleep) so the
            // assertions and the finally-block cleanup don't race the still-open file handle.
            await service.FlushPendingWritesAsync();

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
    public async Task ResetVerificationsForRetry_ResetsNonSkippedToPending_PreservesSkipped_PreservesCommits()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var testConfig = new TempDirConfigService(tempDir);
        var testLogger = new TestLogger();
        var testWatcher = new TestPlanWatcherService();

        var service = new PlanReaderService(
            testConfig,
            testLogger,
            planWatcherService: testWatcher);

        var folderName = "01234-TestPlan";
        var planFolder = Path.Combine(tempDir, folderName);
        Directory.CreateDirectory(planFolder);

        var planYaml = "state: Review\nproject: TestProject\ncommits:\n- abc1234\n- def5678\nverifications:\n- name: Build\n  status: Pass\n- name: Test\n  status: Fail\n- name: Lint\n  status: Skipped\n- name: Format\n  status: Pending\n";
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);

        try
        {
            // Act
            service.ResetVerificationsForRetry(folderName);
            await service.FlushPendingWritesAsync();

            // Assert
            var result = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));
            Assert.Contains("status: Pending", result);
            Assert.Contains("status: Skipped", result);
            Assert.DoesNotContain("status: Pass", result);
            Assert.DoesNotContain("status: Fail", result);
            Assert.Contains("abc1234", result);
            Assert.Contains("def5678", result);
            Assert.Contains(folderName, testWatcher.NotifiedFolders);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SetVerificationStatus_TogglesStatus_PersistsAndPreservesOthers_NotifiesWatcher()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var testConfig = new TempDirConfigService(tempDir);
        var testLogger = new TestLogger();
        var testWatcher = new TestPlanWatcherService();

        var service = new PlanReaderService(
            testConfig,
            testLogger,
            planWatcherService: testWatcher);

        var folderName = "01234-TestPlan";
        var planFolder = Path.Combine(tempDir, folderName);
        Directory.CreateDirectory(planFolder);

        // repos must point at an existing directory to pass plan validation
        var planYaml =
            $"state: Draft\nproject: TestProject\ntitle: TestPlan\nrepos:\n- {tempDir.Replace("\\", "/")}\n" +
            "created: 2026-01-01T00:00:00Z\nupdated: 2026-01-01T00:00:00Z\n" +
            "verifications:\n- name: Build\n  status: Pending\n- name: Test\n  status: Pending\n";
        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), planYaml);

        try
        {
            // Act — skip Test, leave Build untouched
            service.SetVerificationStatus(folderName, "Test", VerificationStatus.Skipped);

            // Assert — written synchronously, so re-read straight away
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            Assert.Equal(VerificationStatus.Pending,
                plan.Verifications.First(v => v.Name == "Build").Status);
            Assert.Equal(VerificationStatus.Skipped,
                plan.Verifications.First(v => v.Name == "Test").Status);
            Assert.Contains(folderName, testWatcher.NotifiedFolders);

            // Act again — toggle back to Pending
            service.SetVerificationStatus(folderName, "Test", VerificationStatus.Pending);
            plan = PlanCommandHelpers.ReadPlan(planFolder);
            Assert.Equal(VerificationStatus.Pending,
                plan.Verifications.First(v => v.Name == "Test").Status);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private class TempDirConfigService(string planFolder) : StubConfigService, IConfigService
    {
        string IConfigService.PlanFolder => planFolder;
    }

    private class TestPlanWatcherService : IPlanWatcherService
    {
        public List<string> NotifiedFolders { get; } = new();
#pragma warning disable CS0067
        public event Action<string?>? PlansChanged;
#pragma warning restore CS0067

        public void NotifyChanged(string? folderName = null)
        {
            if (folderName != null)
                NotifiedFolders.Add(folderName);
        }

        public void Dispose()
        {
        }
    }

    private class TestLogger : ILogger<PlanReaderService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
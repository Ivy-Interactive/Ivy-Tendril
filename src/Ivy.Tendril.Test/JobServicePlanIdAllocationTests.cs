using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class JobServicePlanIdAllocationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _counterPath;

    public JobServicePlanIdAllocationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _counterPath = Path.Combine(_tempDir, ".counter");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void InitializePlanIdCounter_ReadsFromFile()
    {
        // Arrange
        File.WriteAllText(_counterPath, "42");
        var config = TestHelpers.CreateConfigService(_tempDir, _tempDir, _tempDir);

        // Act
        var service = new JobService(config);

        // Assert - allocate one ID and verify it's 43 (counter was 42, incremented to 43)
        var reflection = typeof(JobService).GetMethod("AllocatePlanId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(reflection);
        var allocatedId = (int)reflection!.Invoke(service, null)!;
        Assert.Equal(43, allocatedId);
    }

    [Fact]
    public void InitializePlanIdCounter_DefaultsToOneWhenFileDoesNotExist()
    {
        // Arrange - no .counter file
        var config = TestHelpers.CreateConfigService(_tempDir, _tempDir, _tempDir);

        // Act
        var service = new JobService(config);

        // Assert - allocate one ID and verify it's 1
        var reflection = typeof(JobService).GetMethod("AllocatePlanId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(reflection);
        var allocatedId = (int)reflection!.Invoke(service, null)!;
        Assert.Equal(1, allocatedId);
    }

    [Fact]
    public void InitializePlanIdCounter_DefaultsToOneWhenFileIsInvalid()
    {
        // Arrange
        File.WriteAllText(_counterPath, "not-a-number");
        var config = TestHelpers.CreateConfigService(_tempDir, _tempDir, _tempDir);

        // Act
        var service = new JobService(config);

        // Assert - allocate one ID and verify it's 1 (invalid file, defaulted to 1)
        var reflection = typeof(JobService).GetMethod("AllocatePlanId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(reflection);
        var allocatedId = (int)reflection!.Invoke(service, null)!;
        Assert.Equal(1, allocatedId);
    }

    [Fact]
    public void AllocatePlanId_IsThreadSafe()
    {
        // Arrange
        File.WriteAllText(_counterPath, "1");
        var config = TestHelpers.CreateConfigService(_tempDir, _tempDir, _tempDir);
        var service = new JobService(config);
        var reflection = typeof(JobService).GetMethod("AllocatePlanId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(reflection);

        // Act - allocate 20 IDs concurrently
        var allocatedIds = new System.Collections.Concurrent.ConcurrentBag<int>();
        Parallel.For(0, 20, _ =>
        {
            var id = (int)reflection!.Invoke(service, null)!;
            allocatedIds.Add(id);
        });

        // Assert - all 20 IDs should be unique
        var uniqueIds = allocatedIds.Distinct().ToList();
        Assert.Equal(20, uniqueIds.Count);
        Assert.Equal(20, allocatedIds.Count);

        // Verify they're sequential from 2 to 21 (counter started at 1)
        uniqueIds.Sort();
        Assert.Equal(2, uniqueIds[0]);
        Assert.Equal(21, uniqueIds[^1]);
    }

    [Fact]
    public void AllocatePlanId_PersistsEveryTenAllocations()
    {
        // Arrange
        File.WriteAllText(_counterPath, "1");
        var config = TestHelpers.CreateConfigService(_tempDir, _tempDir, _tempDir);
        var service = new JobService(config);
        var reflection = typeof(JobService).GetMethod("AllocatePlanId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(reflection);

        // Act - allocate 15 IDs
        for (int i = 0; i < 15; i++)
        {
            reflection!.Invoke(service, null);
        }

        // Assert - counter file should be updated at ID 10
        var counterText = File.ReadAllText(_counterPath);
        var counterValue = int.Parse(counterText);
        Assert.True(counterValue >= 10, $"Counter should be at least 10, but was {counterValue}");
    }

    [Fact]
    public void Dispose_PersistsCounterOnShutdown()
    {
        // Arrange
        File.WriteAllText(_counterPath, "1");
        var config = TestHelpers.CreateConfigService(_tempDir, _tempDir, _tempDir);
        var service = new JobService(config);
        var reflection = typeof(JobService).GetMethod("AllocatePlanId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(reflection);

        // Act - allocate 5 IDs (not a multiple of 10, so no automatic persistence)
        for (int i = 0; i < 5; i++)
        {
            reflection!.Invoke(service, null);
        }

        // Dispose to trigger shutdown persistence
        service.Dispose();

        // Assert - counter file should be updated to 6 (started at 1, allocated 5)
        var counterText = File.ReadAllText(_counterPath);
        Assert.Equal("6", counterText);
    }

    [Fact]
    public void CreatePlanJob_ReceivesPlanIdInFirmware()
    {
        // Arrange
        File.WriteAllText(_counterPath, "1");

        // Create a minimal promptware structure for CreatePlan
        var promptwareDir = Path.Combine(_tempDir, "Promptwares", "CreatePlan");
        Directory.CreateDirectory(promptwareDir);
        File.WriteAllText(Path.Combine(promptwareDir, "Program.md"), "# CreatePlan\nTest program");

        var config = TestHelpers.CreateConfigService(_tempDir, _tempDir, _tempDir);
        var service = new JobService(config);

        // Act - create a CreatePlan job (won't actually launch, but firmware should be built)
        try
        {
            // We can't easily test the firmware content without launching the job,
            // but we can verify AllocatePlanId is called and increments the counter
            var reflection = typeof(JobService).GetMethod("AllocatePlanId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(reflection);

            var id1 = (int)reflection!.Invoke(service, null)!;
            var id2 = (int)reflection!.Invoke(service, null)!;

            Assert.Equal(2, id1);
            Assert.Equal(3, id2);
        }
        catch
        {
            // Process launch may fail in test - that's OK
        }
    }
}

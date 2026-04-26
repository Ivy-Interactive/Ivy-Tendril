using System.Diagnostics;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test;

public class PlanYamlHelperAllocateIdTests
{
    [Fact]
    public void AllocatePlanId_SkipsExistingFolders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, ".counter"), "5");
            Directory.CreateDirectory(Path.Combine(tempDir, "00005-ExistingPlan"));

            var id = PlanYamlHelper.AllocatePlanId(tempDir);

            Assert.Equal("00006", id);
            Assert.Equal("7", File.ReadAllText(Path.Combine(tempDir, ".counter")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AllocatePlanId_SkipsMultipleConsecutiveCollisions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, ".counter"), "10");
            Directory.CreateDirectory(Path.Combine(tempDir, "00010-PlanA"));
            Directory.CreateDirectory(Path.Combine(tempDir, "00011-PlanB"));
            Directory.CreateDirectory(Path.Combine(tempDir, "00012-PlanC"));

            var id = PlanYamlHelper.AllocatePlanId(tempDir);

            Assert.Equal("00013", id);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AllocatePlanId_NoCollision_UsesCounterDirectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, ".counter"), "42");

            var id = PlanYamlHelper.AllocatePlanId(tempDir);

            Assert.Equal("00042", id);
            Assert.Equal("43", File.ReadAllText(Path.Combine(tempDir, ".counter")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AllocatePlanId_ConcurrentProcesses_NoDuplicateIds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, ".counter"), "1");

            // Use Task.Run to simulate concurrent allocations from different threads
            // This tests file-based locking across threads (simulating multiple processes)
            var tasks = new List<Task<List<string>>>();

            for (int i = 0; i < 5; i++)
            {
                var task = Task.Run(() =>
                {
                    var ids = new List<string>();
                    for (int j = 0; j < 10; j++)
                    {
                        var id = PlanYamlHelper.AllocatePlanId(tempDir);
                        ids.Add(id);
                        Thread.Sleep(1); // Small delay to increase contention
                    }
                    return ids;
                });
                tasks.Add(task);
            }

            // Wait for all tasks to complete
            Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(30));

            // Collect all allocated IDs
            var allocatedIds = new HashSet<string>();
            foreach (var task in tasks)
            {
                foreach (var id in task.Result)
                {
                    Assert.True(allocatedIds.Add(id), $"Duplicate ID found: {id}");
                }
            }

            // Should have 50 unique IDs (5 tasks * 10 IDs each)
            Assert.Equal(50, allocatedIds.Count);

            // Verify counter was incremented correctly
            var finalCounter = int.Parse(File.ReadAllText(Path.Combine(tempDir, ".counter")));
            Assert.True(finalCounter >= 51, $"Counter should be at least 51, but was {finalCounter}");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AllocatePlanId_CorruptedCounterFile_Recovers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Write invalid data to counter file
            File.WriteAllText(Path.Combine(tempDir, ".counter"), "not-a-number");

            var id = PlanYamlHelper.AllocatePlanId(tempDir);

            // Should recover and start from 1
            Assert.Equal("00001", id);
            Assert.Equal("2", File.ReadAllText(Path.Combine(tempDir, ".counter")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}

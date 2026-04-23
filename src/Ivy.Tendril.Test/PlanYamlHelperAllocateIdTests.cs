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
}

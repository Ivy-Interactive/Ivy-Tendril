using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ivy.Tendril.Test;

public class ExecutePlanProfileOverrideTests
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    [Fact]
    public void PlanYaml_DeserializesExecutionProfile()
    {
        var yaml = """
                   state: Draft
                   project: Tendril
                   level: NiceToHave
                   title: Test Plan
                   executionProfile: balanced
                   repos: []
                   prs: []
                   commits: []
                   verifications: []
                   relatedPlans: []
                   dependsOn: []
                   """;

        var result = Deserializer.Deserialize<PlanYaml>(yaml);

        Assert.Equal("balanced", result.ExecutionProfile);
    }

    [Theory]
    [InlineData("deep")]
    [InlineData("balanced")]
    [InlineData("quick")]
    public void PlanYaml_DeserializesAllProfileValues(string profile)
    {
        var yaml = $"state: Draft\nexecutionProfile: {profile}\n";

        var result = Deserializer.Deserialize<PlanYaml>(yaml);

        Assert.Equal(profile, result.ExecutionProfile);
    }

    [Fact]
    public void PlanYaml_ExecutionProfileIsNullWhenOmitted()
    {
        var yaml = """
                   state: Draft
                   project: Tendril
                   level: NiceToHave
                   title: Test Plan
                   repos: []
                   prs: []
                   commits: []
                   verifications: []
                   relatedPlans: []
                   dependsOn: []
                   """;

        var result = Deserializer.Deserialize<PlanYaml>(yaml);

        Assert.Null(result.ExecutionProfile);
    }

    [Fact]
    public void PlanYaml_ReadPlanYaml_ParsesExecutionProfile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var yamlContent = "state: Draft\nproject: TestProject\nexecutionProfile: quick\n";
            File.WriteAllText(Path.Combine(tempDir, "plan.yaml"), yamlContent);

            var result = JobService.ReadPlanYaml(tempDir);

            Assert.NotNull(result);
            Assert.Equal("quick", result.ExecutionProfile);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PlanYaml_ReadPlanYaml_ExecutionProfileNullWhenMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tendril-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var yamlContent = "state: Draft\nproject: TestProject\n";
            File.WriteAllText(Path.Combine(tempDir, "plan.yaml"), yamlContent);

            var result = JobService.ReadPlanYaml(tempDir);

            Assert.NotNull(result);
            Assert.Null(result.ExecutionProfile);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void NormalizePlanYaml_PreservesExecutionProfile()
    {
        var yaml = "state: Draft\nexecutionProfile: deep\nproject: Tendril\n";

        var repaired = PlanReaderService.RepairPlanYaml(yaml);

        Assert.Contains("executionProfile: deep", repaired);
    }

    [Fact]
    public void NormalizePlanYaml_StripsUnknownFieldButKeepsExecutionProfile()
    {
        var yaml = "state: Draft\nexecutionProfile: balanced\nunknownField: value\nproject: Tendril\n";

        var repaired = PlanReaderService.RepairPlanYaml(yaml);

        Assert.Contains("executionProfile: balanced", repaired);
        Assert.DoesNotContain("unknownField", repaired);
    }
}

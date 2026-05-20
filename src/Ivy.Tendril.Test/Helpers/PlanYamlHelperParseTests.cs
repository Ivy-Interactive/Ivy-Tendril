using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

public class PlanYamlHelperParseTests
{
    [Fact]
    public void ParsePlanYaml_ValidYaml_ReturnsPlanYaml()
    {
        var yaml = """
            title: Fix login bug
            state: InProgress
            project: MyProject
            level: Important
            repos:
              - /repo/one
            """;

        var result = PlanYamlHelper.ParsePlanYaml(yaml);

        Assert.NotNull(result);
        Assert.Equal("Fix login bug", result.Title);
        Assert.Equal("InProgress", result.State);
        Assert.Equal("MyProject", result.Project);
        Assert.Equal("Important", result.Level);
        Assert.Single(result.Repos);
        Assert.Equal("/repo/one", result.Repos[0]);
    }

    [Fact]
    public void ParsePlanYaml_InvalidYaml_ReturnsNull()
    {
        var yaml = "{{{{not valid yaml at all: [[[";

        var result = PlanYamlHelper.ParsePlanYaml(yaml);

        Assert.Null(result);
    }

    [Fact]
    public void ParsePlanYaml_EmptyString_ReturnsNull()
    {
        var result = PlanYamlHelper.ParsePlanYaml("");

        Assert.Null(result);
    }

    [Fact]
    public void ParsePlanYaml_MinimalYaml_ReturnsDefaults()
    {
        var yaml = "title: Simple";

        var result = PlanYamlHelper.ParsePlanYaml(yaml);

        Assert.NotNull(result);
        Assert.Equal("Simple", result.Title);
        Assert.Equal("Draft", result.State);
        Assert.Equal("Auto", result.Project);
    }

    [Fact]
    public void ParsePlanYaml_WithExecutionProfile_Parses()
    {
        var yaml = """
            title: Build feature
            executionProfile: fast
            """;

        var result = PlanYamlHelper.ParsePlanYaml(yaml);

        Assert.NotNull(result);
        Assert.Equal("fast", result.ExecutionProfile);
    }
}

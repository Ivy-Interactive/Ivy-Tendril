using System.IO;
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

    [Fact]
    public void UpdatePlanYamlFields_ReplacesExistingFields_WhenKeyExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PlanYamlUpdateTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var initialYaml = """
                title: Old Title
                state: Draft
                """;
            File.WriteAllText(Path.Combine(tempDir, "plan.yaml"), initialYaml);

            PlanYamlHelper.UpdatePlanYamlFields(tempDir, ("title", "New Title"), ("state", "Executing"));

            var updatedContent = File.ReadAllText(Path.Combine(tempDir, "plan.yaml"));
            Assert.Contains("title: New Title", updatedContent);
            Assert.Contains("state: Executing", updatedContent);
            Assert.DoesNotContain("Old Title", updatedContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void UpdatePlanYamlFields_AppendsMissingFields_WhenKeyDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PlanYamlAppendTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var initialYaml = """
                title: Feature Plan
                state: Draft
                """;
            File.WriteAllText(Path.Combine(tempDir, "plan.yaml"), initialYaml);

            PlanYamlHelper.UpdatePlanYamlFields(tempDir, ("chatSessionId", "sess-abc-123"));

            var updatedContent = File.ReadAllText(Path.Combine(tempDir, "plan.yaml"));
            Assert.Contains("title: Feature Plan", updatedContent);
            Assert.Contains("chatSessionId: sess-abc-123", updatedContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void UpdatePlanYamlFields_MixedUpdates_ReplacesAndAppendsCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PlanYamlMixedTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var initialYaml = "title: Feature Plan\nstate: Draft\n";
            File.WriteAllText(Path.Combine(tempDir, "plan.yaml"), initialYaml);

            PlanYamlHelper.UpdatePlanYamlFields(tempDir, ("state", "Executing"), ("chatSessionId", "sess-999"));

            var updatedContent = File.ReadAllText(Path.Combine(tempDir, "plan.yaml"));
            Assert.Contains("title: Feature Plan", updatedContent);
            Assert.Contains("state: Executing", updatedContent);
            Assert.Contains("chatSessionId: sess-999", updatedContent);
            Assert.DoesNotContain("state: Draft", updatedContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}

public class PlanYamlHelperTests : PlanYamlHelperParseTests { }

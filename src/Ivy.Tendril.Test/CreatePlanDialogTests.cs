using Ivy;
using Ivy.Tendril.Apps.Drafts.Dialogs;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class CreatePlanDialogTests
{
    [Fact]
    public void BuildAgentPrompt_SingleProject_UsesSingularProject()
    {
        var prompt = CreatePlanDialog.BuildAgentPrompt("Tendril-Services", "Make a md5 tool");

        Assert.Equal(
            "I want to discuss creating a Tendril plan for the project Tendril-Services from this description: \"Make a md5 tool\"",
            prompt);
    }

    [Fact]
    public void BuildAgentPrompt_Auto_LetsAgentPickProject()
    {
        var prompt = CreatePlanDialog.BuildAgentPrompt("Auto", "Make a md5 tool");

        Assert.Equal(
            "I want to discuss creating a Tendril plan from this description: \"Make a md5 tool\". Determine the most appropriate project for it yourself.",
            prompt);
    }

    [Fact]
    public void BuildAgentPrompt_EmptyProject_LetsAgentPickProject()
    {
        var prompt = CreatePlanDialog.BuildAgentPrompt("", "Make a md5 tool");

        Assert.Equal(
            "I want to discuss creating a Tendril plan from this description: \"Make a md5 tool\". Determine the most appropriate project for it yourself.",
            prompt);
    }

    [Fact]
    public void BuildAgentPrompt_TrimsDescriptionWhitespace()
    {
        var prompt = CreatePlanDialog.BuildAgentPrompt("Tendril-Services", "  Make a md5 tool  ");

        Assert.Contains("description: \"Make a md5 tool\"", prompt);
        Assert.DoesNotContain("md5 tool \"", prompt);
    }

    [Fact]
    public void BuildAgentPrompt_WithProjectConfig_IncludesRepositoryPaths()
    {
        var config = new ProjectConfig
        {
            Name = "Tendril-Services",
            Repos =
            [
                new RepoRef { Path = "/repos/tendril", BaseBranch = "development" }
            ],
            Verifications =
            [
                new ProjectVerificationRef { Name = "DotnetBuild", Required = true }
            ],
            Context = "High priority service"
        };

        var prompt = CreatePlanDialog.BuildAgentPrompt("Tendril-Services", "Make a md5 tool", config);

        Assert.Contains("project Tendril-Services from this description: \"Make a md5 tool\"", prompt);
        Assert.Contains("/repos/tendril (branch: development)", prompt);
        Assert.Contains("DotnetBuild", prompt);
        Assert.Contains("High priority service", prompt);
        Assert.Contains("tendril job start CreatePlan --description=\"...\" --project=\"Tendril-Services\"", prompt);
    }

    [Fact]
    public void BuildAgentPrompt_WithAttachedFiles_IncludesAttachmentList()
    {
        var files = new[] { "/tmp/screenshot.png", "/tmp/log.txt" };
        var prompt = CreatePlanDialog.BuildAgentPrompt("Tendril-Services", "Fix bug", attachedFiles: files);

        Assert.Contains("### Attached Files", prompt);
        Assert.Contains("/tmp/screenshot.png", prompt);
        Assert.Contains("/tmp/log.txt", prompt);
    }

    [Fact]
    public void BuildAgentPrompt_WithAutoProject_IncludesAvailableProjectsList()
    {
        var projects = new[]
        {
            new ProjectConfig
            {
                Name = "ProjectA",
                Repos = [new RepoRef { Path = "/repos/a", BaseBranch = "main" }]
            },
            new ProjectConfig
            {
                Name = "ProjectB",
                Repos = [new RepoRef { Path = "/repos/b" }]
            }
        };

        var prompt = CreatePlanDialog.BuildAgentPrompt("Auto", "Fix bug", availableProjects: projects);

        Assert.Contains("Determine the most appropriate project for it yourself.", prompt);
        Assert.Contains("### Available Projects", prompt);
        Assert.Contains("ProjectA", prompt);
        Assert.Contains("/repos/a (branch: main)", prompt);
        Assert.Contains("ProjectB", prompt);
        Assert.Contains("/repos/b", prompt);
        Assert.Contains("tendril job start CreatePlan --description=\"...\" --project=\"<project-name>\"", prompt);
    }

    [Fact]
    public void BuildProjectPickerOptions_MultipleProjects_ReturnsAutoFirstThenEachProject()
    {
        var (options, _) = CreatePlanDialog.BuildProjectPickerOptions(["Tendril-Services", "lots-of-dev-tools"]);

        Assert.Equal(3, options.Length);
        Assert.Equal("Auto", options[0].Value);
        Assert.False(options[0].Removable);
        Assert.Equal("Tendril-Services", options[1].Value);
        Assert.Equal("lots-of-dev-tools", options[2].Value);
    }

    [Fact]
    public void BuildProjectPickerOptions_SingleProject_OmitsAuto()
    {
        var (options, _) = CreatePlanDialog.BuildProjectPickerOptions(["Tendril-Services"]);

        Assert.Single(options);
        Assert.Equal("Tendril-Services", options[0].Value);
    }

    [Fact]
    public void BuildProjectPickerOptions_ReturnsExactlyOneAddProjectAction()
    {
        var (options, actions) = CreatePlanDialog.BuildProjectPickerOptions(["Tendril-Services", "lots-of-dev-tools"]);

        Assert.Single(actions);
        Assert.Equal(CreatePlanDialog.AddProjectActionValue, actions[0].Value);
        Assert.Equal("Add Project", actions[0].Label);
        Assert.DoesNotContain(options, o => o.Value == CreatePlanDialog.AddProjectActionValue);
    }

    [Theory]
    [InlineData(0, SelectInputVariant.Toggle)]
    [InlineData(1, SelectInputVariant.Toggle)]
    [InlineData(6, SelectInputVariant.Toggle)]
    [InlineData(7, SelectInputVariant.Select)]
    [InlineData(10, SelectInputVariant.Select)]
    public void GetProjectPickerVariant_SelectsCorrectVariantBasedOnCount(int count, SelectInputVariant expected)
    {
        var variant = CreatePlanDialog.GetProjectPickerVariant(count);

        Assert.Equal(expected, variant);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(12, true)]
    public void IsProjectPickerSearchable_EnablesSearchOnlyWhenMoreThanSixProjects(int count, bool expected)
    {
        var searchable = CreatePlanDialog.IsProjectPickerSearchable(count);

        Assert.Equal(expected, searchable);
    }

    [Fact]
    public void BuildProjectSelectOptions_MultipleProjects_IncludesAutoAndAddProject()
    {
        var projects = new[] { "p1", "p2", "p3", "p4", "p5", "p6", "p7" };
        var options = CreatePlanDialog.BuildProjectSelectOptions(projects);

        Assert.Equal(9, options.Count);
        Assert.Equal("Auto", options[0].Value);
        for (var i = 0; i < projects.Length; i++)
        {
            Assert.Equal(projects[i], options[i + 1].Value);
        }
        Assert.Equal(CreatePlanDialog.AddProjectActionValue, options[8].Value);
    }

    [Fact]
    public void BuildProjectSelectOptions_SingleProject_OmitsAutoAndIncludesAddProject()
    {
        var projects = new[] { "Tendril-Services" };
        var options = CreatePlanDialog.BuildProjectSelectOptions(projects);

        Assert.Equal(2, options.Count);
        Assert.Equal("Tendril-Services", options[0].Value);
        Assert.Equal(CreatePlanDialog.AddProjectActionValue, options[1].Value);
    }
}


using Ivy.Tendril.Apps.Drafts.Dialogs;

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
}

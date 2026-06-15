using Ivy.Tendril.Apps.Drafts.Dialogs;

namespace Ivy.Tendril.Test;

public class CreatePlanDialogTests
{
    [Fact]
    public void BuildAgentPrompt_SingleProject_UsesSingularProject()
    {
        var prompt = CreatePlanDialog.BuildAgentPrompt(["Tendril-Services"], "Make a md5 tool");

        Assert.Equal(
            "I want to discuss creating a Tendril plan for the project Tendril-Services from this description: \"Make a md5 tool\"",
            prompt);
    }

    [Fact]
    public void BuildAgentPrompt_MultipleProjects_UsesPluralAndOr()
    {
        var prompt = CreatePlanDialog.BuildAgentPrompt(["Tendril-Services", "lots-of-dev-tools"], "Make a md5 tool");

        Assert.Equal(
            "I want to discuss creating a Tendril plan for the projects Tendril-Services or lots-of-dev-tools from this description: \"Make a md5 tool\"",
            prompt);
    }

    [Fact]
    public void BuildAgentPrompt_Auto_LetsAgentPickProject()
    {
        var prompt = CreatePlanDialog.BuildAgentPrompt(["Auto"], "Make a md5 tool");

        Assert.Equal(
            "I want to discuss creating a Tendril plan from this description: \"Make a md5 tool\". Determine the most appropriate project for it yourself.",
            prompt);
    }

    [Fact]
    public void BuildAgentPrompt_TrimsDescriptionWhitespace()
    {
        var prompt = CreatePlanDialog.BuildAgentPrompt(["Tendril-Services"], "  Make a md5 tool  ");

        Assert.Contains("description: \"Make a md5 tool\"", prompt);
        Assert.DoesNotContain("md5 tool \"", prompt);
    }
}

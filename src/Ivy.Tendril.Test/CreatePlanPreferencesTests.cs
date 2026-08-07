using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class CreatePlanPreferencesTests
{
    [Fact]
    public void DefaultValue_IsAuto()
    {
        var preferences = new CreatePlanPreferences();

        Assert.Equal("Auto", preferences.LastSelectedProject);
    }

    [Fact]
    public void SharedInstance_PreservesSelection()
    {
        var sharedPreferences = new CreatePlanPreferences();

        sharedPreferences.LastSelectedProject = "Tendril";
        var retrieved = sharedPreferences.LastSelectedProject;

        Assert.Equal("Tendril", retrieved);
    }

    [Fact]
    public void MultipleReferences_ShareState()
    {
        ICreatePlanPreferences preferences = new CreatePlanPreferences();
        ICreatePlanPreferences sameReference = preferences;

        preferences.LastSelectedProject = "ProjectA";

        Assert.Equal("ProjectA", sameReference.LastSelectedProject);
    }
}

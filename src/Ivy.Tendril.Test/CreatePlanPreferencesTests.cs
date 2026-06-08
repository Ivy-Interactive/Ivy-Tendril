using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class CreatePlanPreferencesTests
{
    [Fact]
    public void DefaultValue_IsAuto()
    {
        var preferences = new CreatePlanPreferences();

        Assert.Single(preferences.LastSelectedProjects);
        Assert.Equal("Auto", preferences.LastSelectedProjects[0]);
    }

    [Fact]
    public void SharedInstance_PreservesSelection()
    {
        var sharedPreferences = new CreatePlanPreferences();

        sharedPreferences.LastSelectedProjects = ["Tendril", "Other"];
        var retrieved = sharedPreferences.LastSelectedProjects;

        Assert.Equal(2, retrieved.Length);
        Assert.Equal("Tendril", retrieved[0]);
        Assert.Equal("Other", retrieved[1]);
    }

    [Fact]
    public void MultipleReferences_ShareState()
    {
        ICreatePlanPreferences preferences = new CreatePlanPreferences();
        ICreatePlanPreferences sameReference = preferences;

        preferences.LastSelectedProjects = ["ProjectA"];

        Assert.Single(sameReference.LastSelectedProjects);
        Assert.Equal("ProjectA", sameReference.LastSelectedProjects[0]);
    }
}

namespace Ivy.Tendril.Services;

public class CreatePlanPreferences : ICreatePlanPreferences
{
    public string[] LastSelectedProjects { get; set; } = ["Auto"];
}

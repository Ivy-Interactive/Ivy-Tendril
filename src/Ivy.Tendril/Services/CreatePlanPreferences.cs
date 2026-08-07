namespace Ivy.Tendril.Services;

public class CreatePlanPreferences : ICreatePlanPreferences
{
    public string LastSelectedProject { get; set; } = "Auto";
}

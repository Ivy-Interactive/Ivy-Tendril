namespace Ivy.Tendril.Services.Plans;

public interface IPlanCountsService : IDisposable
{
    PlanCounts Current { get; }
    event Action? CountsChanged;
    void RefreshNow();
}
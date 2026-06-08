namespace Ivy.Tendril.Services.Plans;

public record TendrilProcessStatus
{
    public int IceboxCount { get; init; }
    public int JobCount { get; init; }
    public int TrashCount { get; init; }
    public int DraftCount { get; init; }
    public int ReviewCount { get; init; }
    public int CreatingPlansCount { get; init; }
    public int UpdatingPlansCount { get; init; }
    public int ExecutingPlansCount { get; init; }
    public int RetryingPlansCount { get; init; }
    public int CreatingPrCount { get; init; }
    public int RecommendationsCount { get; init; }

    public static TendrilProcessStatus Empty => new();
}

public interface ITendrilProcessStatusService : IDisposable
{
    IObservable<TendrilProcessStatus> Status { get; }
    TendrilProcessStatus Current { get; }
    void RefreshNow();
}

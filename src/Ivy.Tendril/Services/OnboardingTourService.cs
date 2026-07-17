namespace Ivy.Tendril.Services;

public interface IOnboardingTourService
{
    /// <summary>Zero-based index of the active tour step, or null when no tour is running.</summary>
    int? Step { get; }
    event Action? Changed;
    void Start();
    void SetStep(int step);
    void Dismiss();
}

public class OnboardingTourService : IOnboardingTourService
{
    public int? Step { get; private set; }
    public event Action? Changed;

    public void Start()
    {
        Step = 0;
        Changed?.Invoke();
    }

    public void SetStep(int step)
    {
        Step = step;
        Changed?.Invoke();
    }

    public void Dismiss()
    {
        Step = null;
        Changed?.Invoke();
    }
}

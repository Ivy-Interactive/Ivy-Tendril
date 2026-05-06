namespace Ivy.Tendril.Services;

public interface IOnboardingSetupService
{
    Task BootstrapTendrilHomeAsync(string tendrilHome);
    Task FinalizeOnboardingAsync();
    Task StartBackgroundServicesAsync();
}

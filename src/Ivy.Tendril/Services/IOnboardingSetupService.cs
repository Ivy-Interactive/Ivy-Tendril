namespace Ivy.Tendril.Services;

public interface IOnboardingSetupService
{
    Task BootstrapTendrilHomeAsync(string tendrilHome);
    Task CommitPendingProjectAsync();
    Task FinalizeOnboardingAsync();
    Task RemoveProjectVerificationAsync(string projectName, string verificationName);
    Task StartBackgroundServicesAsync();
}

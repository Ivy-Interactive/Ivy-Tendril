using Ivy.Tendril.Test.End2End.Configuration;

namespace Ivy.Tendril.Test.End2End.Fixtures;

[CollectionDefinition("E2E")]
public class E2ETestCollection : ICollectionFixture<E2ETestFixture> { }

public class E2ETestFixture : IAsyncLifetime
{
    public TendrilProcessFixture Tendril { get; } = new();
    public PlaywrightFixture Playwright { get; } = new();
    public TestRepositoryFixture TestRepo { get; } = new();
    public E2ETestSettings Settings => TestSettingsProvider.Get();

    public bool OnboardingCompleted { get; set; }

    public async Task InitializeAsync()
    {
        // Playwright and repo fork can start in parallel
        var playwrightTask = Playwright.InitializeAsync();
        var repoTask = TestRepo.InitializeAsync();
        await Task.WhenAll(playwrightTask, repoTask);

        // Tendril must start after repo is ready (onboarding references repo path)
        await Tendril.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await Tendril.DisposeAsync();
        await Playwright.DisposeAsync();
        await TestRepo.DisposeAsync();
    }
}

using Ivy.Tendril.Test.End2End.Configuration;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Fixtures;

[CollectionDefinition("E2E-Promptware")]
public class PromptwareCollection : ICollectionFixture<PromptwareTestFixture> { }

public class PromptwareTestFixture : IAsyncLifetime
{
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];

    public string TendrilHome { get; private set; } = "";
    public string PlansDir { get; private set; } = "";
    public string ConfigPath { get; private set; } = "";
    public TestRepositoryFixture TestRepo { get; } = new();
    public PromptwareRunner Runner { get; private set; } = null!;
    public E2ETestSettings Settings { get; } = TestSettingsProvider.Get();

    public async Task InitializeAsync()
    {
        TendrilHome = Path.Combine(Path.GetTempPath(), $"tendril-pw-{_runId}");
        PlansDir = Path.Combine(TendrilHome, "Plans");
        ConfigPath = Path.Combine(TendrilHome, "config.yaml");

        Directory.CreateDirectory(TendrilHome);
        Directory.CreateDirectory(PlansDir);
        File.WriteAllText(Path.Combine(PlansDir, ".counter"), "0");

        await TestRepo.InitializeAsync();

        WriteConfig();

        Runner = new PromptwareRunner(Settings.TendrilProjectPath, TendrilHome);
    }

    private void WriteConfig()
    {
        var repoPath = TestRepo.LocalClonePath.Replace('\\', '/');
        var yaml = $"""
            codingAgent: {Settings.Agent}
            jobTimeout: 30
            staleOutputTimeout: 10
            maxConcurrentJobs: 5
            projects:
              - name: E2ETest
                repos:
                  - path: "{repoPath}"
            verifications:
              - name: DotnetBuild
                command: dotnet build
            """;

        File.WriteAllText(ConfigPath, yaml);
    }

    public async Task DisposeAsync()
    {
        await TestRepo.DisposeAsync();

        if (Directory.Exists(TendrilHome))
        {
            try
            {
                ClearReadOnlyAttributes(TendrilHome);
                Directory.Delete(TendrilHome, recursive: true);
            }
            catch { }
        }
    }

    private static void ClearReadOnlyAttributes(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }
    }
}

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

    /// <summary>
    /// Scratch space for files the tests themselves produce. Deliberately a sibling of
    /// <see cref="TendrilHome"/>, not a child: everything inside TendrilHome is a directory Tendril
    /// owns and means something, and test instruments should not be mistaken for Tendril's own data.
    /// </summary>
    public string TestArtifactsDir { get; private set; } = "";

    public TestRepositoryFixture TestRepo { get; } = new();
    public PromptwareRunner Runner { get; private set; } = null!;
    public E2ETestSettings Settings { get; } = TestSettingsProvider.Get();

    /// <summary>Path for a <c>--cli-log</c> file: a record of the tendril CLI calls an agent made.</summary>
    public string CliLogPath(string name) => Path.Combine(TestArtifactsDir, $"{name}.cli.jsonl");

    public async Task InitializeAsync()
    {
        TendrilHome = Path.Combine(Path.GetTempPath(), $"tendril-pw-{_runId}");
        PlansDir = Path.Combine(TendrilHome, "Plans");
        ConfigPath = Path.Combine(TendrilHome, "config.yaml");
        TestArtifactsDir = Path.Combine(Path.GetTempPath(), $"tendril-pw-{_runId}-artifacts");

        Directory.CreateDirectory(TendrilHome);
        Directory.CreateDirectory(PlansDir);
        Directory.CreateDirectory(TestArtifactsDir);
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
            promptwares:
              CreatePlan:
                profile: balanced
                allowedTools:
                  - Read
                  - Glob
                  - Grep
                  - Bash
              UpdatePlan:
                profile: balanced
                allowedTools:
                  - Read
                  - Glob
                  - Grep
                  - Bash
              SplitPlan:
                profile: balanced
                allowedTools:
                  - Read
                  - Glob
                  - Grep
                  - Bash
              ExecutePlan:
                profile: deep
                allowedTools:
                  - Read
                  - Write
                  - Edit
                  - Glob
                  - Grep
                  - Bash
            """;

        File.WriteAllText(ConfigPath, yaml);
    }

    public async Task DisposeAsync()
    {
        await TestRepo.DisposeAsync();

        foreach (var dir in new[] { TendrilHome, TestArtifactsDir })
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                ClearReadOnlyAttributes(dir);
                Directory.Delete(dir, recursive: true);
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

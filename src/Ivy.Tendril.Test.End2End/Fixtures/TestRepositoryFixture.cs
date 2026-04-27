using Ivy.Tendril.Test.End2End.Configuration;
using Ivy.Tendril.Test.End2End.Helpers;

namespace Ivy.Tendril.Test.End2End.Fixtures;

public class TestRepositoryFixture : IAsyncLifetime
{
    public string ForkedRepoFullName { get; private set; } = "";
    public string LocalClonePath { get; private set; } = "";
    public string ForkName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var settings = TestSettingsProvider.Get();

        var whoami = await ProcessHelper.RunAsync("gh", "api user -q .login", timeoutMs: 15_000);
        if (whoami.ExitCode != 0)
            throw new InvalidOperationException($"Failed to get GitHub username: {whoami.Error}");
        var username = whoami.Output.Trim();

        // Try multiple fork names to handle stale forks from previous runs
        ProcessHelper.ProcessResult? forkResult = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var runId = Guid.NewGuid().ToString("N")[..12];
            ForkName = $"tendril-e2e-{runId}";
            LocalClonePath = Path.Combine(Path.GetTempPath(), $"tendril-e2e-repo-{runId}");

            forkResult = await ProcessHelper.RunAsync(
                "gh",
                $"repo fork {settings.TestRepo} --clone=false --fork-name {ForkName}",
                timeoutMs: 60_000);

            if (forkResult.ExitCode == 0) break;

            // Wait for GitHub orchestration to settle before retrying
            await Task.Delay(5000);
        }

        if (forkResult == null || forkResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"Failed to fork {settings.TestRepo} after 3 attempts: {forkResult?.Error}");

        ForkedRepoFullName = $"{username}/{ForkName}";

        // Wait for GitHub to finish fork orchestration
        await Task.Delay(3000);

        // Clone to local temp directory
        var cloneResult = await ProcessHelper.RunAsync(
            "git",
            $"clone https://github.com/{ForkedRepoFullName} \"{LocalClonePath}\"",
            timeoutMs: 60_000);

        if (cloneResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"Failed to clone {ForkedRepoFullName}: {cloneResult.Error}");
    }

    public async Task DisposeAsync()
    {
        var settings = TestSettingsProvider.Get();

        await TryDeleteDirectoryAsync(LocalClonePath);

        if (settings.CleanupFork && !string.IsNullOrEmpty(ForkedRepoFullName))
        {
            try
            {
                await ProcessHelper.RunAsync(
                    "gh",
                    $"repo delete {ForkedRepoFullName} --yes",
                    timeoutMs: 30_000);
            }
            catch { /* Best-effort — requires delete_repo scope */ }
        }
    }

    private static async Task TryDeleteDirectoryAsync(string path, int maxAttempts = 3)
    {
        if (!Directory.Exists(path)) return;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                ClearReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (i < maxAttempts - 1)
            {
                await Task.Delay(500 * (i + 1));
            }
            catch (UnauthorizedAccessException) when (i < maxAttempts - 1)
            {
                await Task.Delay(500 * (i + 1));
            }
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

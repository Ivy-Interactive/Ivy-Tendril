using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class OnboardingGitignoreTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("gitignore-test");
    private readonly OnboardingSetupService _service;
    private readonly string _tendrilHome;

    public OnboardingGitignoreTests()
    {
        _tendrilHome = Path.Combine(_tempDir.Path, "tendril-home");
        Directory.CreateDirectory(_tendrilHome);
        var config = new ConfigService(new TendrilSettings());
        _service = new OnboardingSetupService(
            config,
            null!,
            NullLogger<OnboardingSetupService>.Instance);
    }

    public void Dispose()
    {
        // Restore git global config to original state if we changed it
        try
        {
            var gitignorePath = Path.Combine(_tempDir.Path, "test-gitignore");
            if (File.Exists(gitignorePath))
                File.Delete(gitignorePath);
        }
        catch { /* best effort */ }

        _tempDir.Dispose();
    }

    [Fact]
    public async Task EnsureGlobalGitignore_CreatesFileWhenNoneExists()
    {
        // The method uses git config and XDG path, so we test the marker file behavior
        // and the overall flow without mocking git
        await _service.EnsureGlobalGitignoreAsync(_tendrilHome);

        // Marker file should be created
        var markerPath = Path.Combine(_tendrilHome, ".gitignore-configured");
        Assert.True(File.Exists(markerPath), "Marker file should be created after running");
    }

    [Fact]
    public async Task EnsureGlobalGitignore_IsIdempotent()
    {
        await _service.EnsureGlobalGitignoreAsync(_tendrilHome);

        // Get the global gitignore path (XDG default or custom)
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdgPath = Path.Combine(home, ".config", "git", "ignore");

        string? gitignoreContent = null;
        if (File.Exists(xdgPath))
            gitignoreContent = await File.ReadAllTextAsync(xdgPath);

        // Run again
        await _service.EnsureGlobalGitignoreAsync(_tendrilHome);

        // Content should not be duplicated
        if (File.Exists(xdgPath))
        {
            var afterContent = await File.ReadAllTextAsync(xdgPath);
            var dsStoreCount = afterContent.Split('\n').Count(l => l.Trim() == ".DS_Store");
            Assert.True(dsStoreCount <= 1, $".DS_Store pattern should appear at most once, found {dsStoreCount} times");
        }
    }

    [Fact]
    public async Task EnsureGlobalGitignore_AppendsToExistingContent()
    {
        // Get the current global gitignore path
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdgPath = Path.Combine(home, ".config", "git", "ignore");
        var dir = Path.GetDirectoryName(xdgPath)!;
        Directory.CreateDirectory(dir);

        // Save original content for restoration
        string? originalContent = null;
        if (File.Exists(xdgPath))
            originalContent = await File.ReadAllTextAsync(xdgPath);

        try
        {
            // Write some pre-existing content
            var preExisting = "# My custom ignores\n*.log\n";
            if (originalContent != null)
                preExisting = originalContent;

            await File.WriteAllTextAsync(xdgPath, preExisting);

            await _service.EnsureGlobalGitignoreAsync(_tendrilHome);

            var content = await File.ReadAllTextAsync(xdgPath);

            // Original content should be preserved
            Assert.Contains(preExisting.TrimEnd(), content);

            // OS metadata patterns should be present
            Assert.Contains(".DS_Store", content);
            Assert.Contains("Thumbs.db", content);
            Assert.Contains("desktop.ini", content);
        }
        finally
        {
            // Restore original content
            if (originalContent != null)
                await File.WriteAllTextAsync(xdgPath, originalContent);
        }
    }

    [Fact]
    public async Task EnsureGlobalGitignoreOnStartup_SkipsWhenMarkerExists()
    {
        // Create marker file
        var markerPath = Path.Combine(_tendrilHome, ".gitignore-configured");
        await File.WriteAllTextAsync(markerPath, DateTime.UtcNow.ToString("O"));
        var markerTime = File.GetLastWriteTimeUtc(markerPath);

        // Short delay to distinguish timestamps
        await Task.Delay(50);

        await _service.EnsureGlobalGitignoreOnStartupAsync(_tendrilHome);

        // Marker file should not have been rewritten
        Assert.Equal(markerTime, File.GetLastWriteTimeUtc(markerPath));
    }

    [Fact]
    public async Task EnsureGlobalGitignoreOnStartup_RunsWhenNoMarker()
    {
        var markerPath = Path.Combine(_tendrilHome, ".gitignore-configured");
        Assert.False(File.Exists(markerPath));

        await _service.EnsureGlobalGitignoreOnStartupAsync(_tendrilHome);

        Assert.True(File.Exists(markerPath), "Marker file should be created when migration runs");
    }
}

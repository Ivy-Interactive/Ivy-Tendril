using Ivy.Tendril.Commands;
using Ivy.Tendril.Commands.DoctorChecks;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class DoctorChecksTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("ivy-doctor-test");

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public async Task EnvironmentCheck_MissingConfigFile_ShowsFullPath()
    {
        var tempDir = _tempDir.Path;
        var expectedConfigPath = Path.Combine(tempDir, "config.yaml");

        // Ensure no config file exists
        if (File.Exists(expectedConfigPath))
            File.Delete(expectedConfigPath);

        Environment.SetEnvironmentVariable("TENDRIL_HOME", tempDir);

        try
        {
            var check = new EnvironmentCheck();
            var result = await check.RunAsync();

            var configStatus = result.Statuses.FirstOrDefault(s => s.Label == "config.yaml");
            Assert.NotNull(configStatus);
            Assert.Equal(StatusKind.Error, configStatus.Kind);
            Assert.Contains("Not found at", configStatus.Value);
            Assert.Contains(expectedConfigPath, configStatus.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", null);
        }
    }

    [Fact]
    public async Task EnvironmentCheck_UnsetTendrilHome_ResolvesDefault()
    {
        var originalEnv = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", null);

        try
        {
            var check = new EnvironmentCheck();
            var result = await check.RunAsync();

            var homeStatus = result.Statuses.FirstOrDefault(s => s.Label == "TENDRIL_HOME");
            Assert.NotNull(homeStatus);
            Assert.Equal(StatusKind.Ok, homeStatus.Kind);
            Assert.Contains("Not set (using default)", homeStatus.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", originalEnv);
        }
    }

    [Fact]
    public async Task InstallationCheck_LegacyDotnetToolPresent_ReportsError()
    {
        var original = TendrilInstallHelper.UserProfileOverride;
        TendrilInstallHelper.UserProfileOverride = _tempDir.Path;

        try
        {
            var storeDir = Path.Combine(_tempDir.Path, ".dotnet", "tools", ".store", "ivy.tendril", "1.0.0");
            Directory.CreateDirectory(storeDir);

            var check = new InstallationCheck();
            var result = await check.RunAsync();

            var legacyStatus = result.Statuses.FirstOrDefault(s => s.Label == "Legacy .NET tool");
            Assert.NotNull(legacyStatus);
            Assert.Equal(StatusKind.Error, legacyStatus.Kind);
            Assert.True(result.HasErrors);
        }
        finally
        {
            TendrilInstallHelper.UserProfileOverride = original;
        }
    }

    [Fact]
    public async Task InstallationCheck_NoLegacyDotnetTool_ReportsOk()
    {
        var original = TendrilInstallHelper.UserProfileOverride;
        TendrilInstallHelper.UserProfileOverride = _tempDir.Path;

        try
        {
            var check = new InstallationCheck();
            var result = await check.RunAsync();

            var legacyStatus = result.Statuses.FirstOrDefault(s => s.Label == "Legacy .NET tool");
            Assert.NotNull(legacyStatus);
            Assert.Equal(StatusKind.Ok, legacyStatus.Kind);
        }
        finally
        {
            TendrilInstallHelper.UserProfileOverride = original;
        }
    }

    [Fact]
    public void PrintStatus_WithBracketCharacters_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            DoctorCommand.PrintStatus(
                "[flags]",
                "[error] markup",
                StatusKind.Ok));

        Assert.Null(exception);
    }
}

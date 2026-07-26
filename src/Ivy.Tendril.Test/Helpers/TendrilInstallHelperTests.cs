using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Helpers;

[Collection("TendrilHome")]
public class TendrilInstallHelperTests
{
    [Fact]
    public void IsLegacyToolInstalled_TogglesWithStoreDirectory()
    {
        using var fixture = new TempDirectoryFixture("ivy-legacy-test");
        var original = TendrilInstallHelper.UserProfileOverride;
        TendrilInstallHelper.UserProfileOverride = fixture.Path;

        try
        {
            Assert.False(TendrilInstallHelper.IsLegacyToolInstalled());

            var storeDir = Path.Combine(fixture.Path, ".dotnet", "tools", ".store", "ivy.tendril", "1.1.0");
            Directory.CreateDirectory(storeDir);

            Assert.True(TendrilInstallHelper.IsLegacyToolInstalled());
        }
        finally
        {
            TendrilInstallHelper.UserProfileOverride = original;
        }
    }

    [Fact]
    public void GetLegacyToolVersion_MultipleVersions_ReturnsNewest()
    {
        using var fixture = new TempDirectoryFixture("ivy-legacy-version-test");
        var original = TendrilInstallHelper.UserProfileOverride;
        TendrilInstallHelper.UserProfileOverride = fixture.Path;

        try
        {
            var storeRoot = Path.Combine(fixture.Path, ".dotnet", "tools", ".store", "ivy.tendril");
            Directory.CreateDirectory(Path.Combine(storeRoot, "1.0.0"));
            Directory.CreateDirectory(Path.Combine(storeRoot, "1.2.0"));
            Directory.CreateDirectory(Path.Combine(storeRoot, "1.1.5"));

            var result = TendrilInstallHelper.GetLegacyToolVersion();

            Assert.Equal("1.2.0", result);
        }
        finally
        {
            TendrilInstallHelper.UserProfileOverride = original;
        }
    }

    [Fact]
    public void FindInstalledCli_NoCandidatesExist_ReturnsNullThenFindsLocalBinFile()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = new TempDirectoryFixture("ivy-install-test");
        var original = TendrilInstallHelper.UserProfileOverride;
        TendrilInstallHelper.UserProfileOverride = fixture.Path;

        try
        {
            var before = TendrilInstallHelper.FindInstalledCli();
            if (before != null)
            {
                // A fixed OS-standard candidate (e.g. /Applications/Ivy Tendril.app or
                // /usr/local/bin/tendril) already resolves on this machine. Those absolute
                // paths are not affected by UserProfileOverride, so the isolated assertion
                // below does not apply here.
                return;
            }

            var localBinDir = Path.Combine(fixture.Path, ".local", "bin");
            Directory.CreateDirectory(localBinDir);
            var tendrilPath = Path.Combine(localBinDir, "tendril");
            File.WriteAllText(tendrilPath, "#!/bin/sh\n");

            var after = TendrilInstallHelper.FindInstalledCli();

            Assert.Equal(tendrilPath, after);
        }
        finally
        {
            TendrilInstallHelper.UserProfileOverride = original;
        }
    }

    [Fact]
    public void FindInstalledCli_SymlinkTargetsLegacyTool_ReturnsNull()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = new TempDirectoryFixture("ivy-install-symlink-test");
        var original = TendrilInstallHelper.UserProfileOverride;
        TendrilInstallHelper.UserProfileOverride = fixture.Path;

        try
        {
            var before = TendrilInstallHelper.FindInstalledCli();
            if (before != null)
            {
                // A fixed OS-standard candidate already resolves on this machine; see above.
                return;
            }

            var legacyToolsDir = Path.Combine(fixture.Path, ".dotnet", "tools");
            Directory.CreateDirectory(legacyToolsDir);
            var legacyExe = Path.Combine(legacyToolsDir, "tendril");
            File.WriteAllText(legacyExe, "#!/bin/sh\n");

            var localBinDir = Path.Combine(fixture.Path, ".local", "bin");
            Directory.CreateDirectory(localBinDir);
            var symlinkPath = Path.Combine(localBinDir, "tendril");
            File.CreateSymbolicLink(symlinkPath, legacyExe);

            var result = TendrilInstallHelper.FindInstalledCli();

            Assert.Null(result);
        }
        finally
        {
            TendrilInstallHelper.UserProfileOverride = original;
        }
    }

    [Fact]
    public void ResolveOnPath_FindsExecutableInPath()
    {
        using var fixture = new TempDirectoryFixture("ivy-resolve-path-test");
        var exeName = OperatingSystem.IsWindows() ? "fake-tendril-tool.exe" : "fake-tendril-tool";
        var exePath = Path.Combine(fixture.Path, exeName);
        File.WriteAllText(exePath, "#!/bin/sh\n");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var separator = OperatingSystem.IsWindows() ? ';' : ':';

        try
        {
            Environment.SetEnvironmentVariable("PATH", fixture.Path + separator + originalPath);

            var result = TendrilInstallHelper.ResolveOnPath("fake-tendril-tool");

            Assert.Equal(exePath, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }
}

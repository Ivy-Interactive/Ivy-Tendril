using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test;

/// <summary>
///     Guards <see cref="TendrilHomeIsolation" />. These assert the *effect* (where a write actually
///     lands) rather than merely that the override property is set, because the defect being guarded
///     against is production state being written to, not a property being unassigned.
/// </summary>
[Collection("TendrilHome")]
public class TendrilHomeIsolationTests
{
    [Fact]
    public void Root_IsAFreshDirectoryUnderTheTempPath()
    {
        Assert.True(Directory.Exists(TendrilHomeIsolation.Root));
        Assert.True(Directory.Exists(TendrilHomeIsolation.PlansRoot));
        Assert.StartsWith(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ivy-tendril-test")),
            Path.GetFullPath(TendrilHomeIsolation.Root),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvedTendrilHome_IsInsideTheIsolatedRoot()
    {
        Assert.True(
            TendrilHomeIsolation.IsInsideRoot(PathHelper.GetDefaultTendrilHome()),
            $"GetDefaultTendrilHome() returned '{PathHelper.GetDefaultTendrilHome()}', " +
            $"which is outside '{TendrilHomeIsolation.Root}'.");
    }

    [Fact]
    public void ProcessEnvironment_NeverPointsAtTheMachinesLiveTendrilHome()
    {
        var home = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        Assert.True(
            TendrilHomeIsolation.IsInsideRoot(home),
            $"TENDRIL_HOME is '{home}', which is outside '{TendrilHomeIsolation.Root}'.");

        foreach (var name in new[] { "TENDRIL_HOME", "TENDRIL_PLANS" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value)) continue;

            foreach (var machineHome in TendrilHomeIsolation.GetMachineTendrilHomes())
                Assert.False(
                    TendrilHomeIsolation.IsInside(value, machineHome),
                    $"{name} is '{value}', which is inside the machine's live Tendril home '{machineHome}'.");
        }
    }

    /// <summary>
    ///     Isolation must not be installed at a layer that outranks the per-test temporary homes.
    ///     <see cref="PathHelper.DefaultTendrilHomeOverride" /> beats <c>TENDRIL_HOME</c> and
    ///     <c>TENDRIL_PLANS</c> beats the resolved home, so pinning either one assembly-wide would send
    ///     every test that installs its own home through <c>TENDRIL_HOME</c> to the shared root instead.
    /// </summary>
    [Fact]
    public void PerTestTendrilHome_StillOutranksTheAssemblyWideIsolation()
    {
        Assert.Null(PathHelper.DefaultTendrilHomeOverride);
        Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TENDRIL_PLANS")));
        Assert.Equal(TendrilHomeIsolation.PlansRoot, PlanCommandHelpers.GetPlansDirectory());

        var perTestHome = Path.Combine(TendrilHomeIsolation.Root, "per-test-home");
        Directory.CreateDirectory(Path.Combine(perTestHome, "Plans"));
        try
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", perTestHome);
            Assert.Equal(
                Path.Combine(perTestHome, "Plans"),
                PlanCommandHelpers.GetPlansDirectory());
        }
        finally
        {
            TendrilHomeIsolation.Apply();
        }
    }

    [Fact]
    public void ResolvedTendrilHome_IsNotTheMachinesLiveHome()
    {
        var resolved = Path.GetFullPath(PathHelper.GetDefaultTendrilHome());

        foreach (var machineHome in TendrilHomeIsolation.GetMachineTendrilHomes())
        {
            Assert.False(
                resolved.Equals(machineHome, StringComparison.OrdinalIgnoreCase),
                $"The suite resolved the machine's live Tendril home '{machineHome}'.");
            Assert.False(TendrilHomeIsolation.IsInsideRoot(machineHome));
        }
    }

    [Fact]
    public void CrashLogWrite_LandsInTheIsolatedRoot_AndNotInTheLiveHome()
    {
        var liveCrashLogs = TendrilHomeIsolation.GetMachineTendrilHomes()
            .Select(home => Path.Combine(home, "crash.log"))
            .ToDictionary(path => path, path => new FileInfo(path) is { Exists: true } fi ? fi.Length : -1L);

        Assert.True(
            TendrilHomeIsolation.IsInsideRoot(Path.GetDirectoryName(TendrilHomeIsolation.CrashLogPath)),
            $"CrashLog resolved to '{TendrilHomeIsolation.CrashLogPath}', " +
            $"which is outside '{TendrilHomeIsolation.Root}'.");

        var marker = $"[TendrilHomeIsolationTests] {Guid.NewGuid():N}";
        Ivy.Helpers.CrashLog.Write(marker);

        Assert.True(File.Exists(TendrilHomeIsolation.CrashLogPath));
        Assert.Contains(marker, File.ReadAllText(TendrilHomeIsolation.CrashLogPath));

        foreach (var (path, lengthBefore) in liveCrashLogs)
        {
            var lengthAfter = new FileInfo(path) is { Exists: true } fi ? fi.Length : -1L;
            Assert.Equal(lengthBefore, lengthAfter);
        }
    }

    [Fact]
    public void Apply_ReinstallsIsolationAfterATestClearsTheEnvironment()
    {
        try
        {
            PathHelper.DefaultTendrilHomeOverride = "D:\\live";
            Environment.SetEnvironmentVariable("TENDRIL_HOME", null);
            Environment.SetEnvironmentVariable("TENDRIL_PLANS", "D:\\live\\Plans");

            TendrilHomeIsolation.Apply();

            Assert.Null(PathHelper.DefaultTendrilHomeOverride);
            Assert.Equal(TendrilHomeIsolation.Root, Environment.GetEnvironmentVariable("TENDRIL_HOME"));
            Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TENDRIL_PLANS")));
            Assert.True(TendrilHomeIsolation.IsInsideRoot(PathHelper.GetDefaultTendrilHome()));
        }
        finally
        {
            TendrilHomeIsolation.Apply();
        }
    }
}

using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test;

/// <summary>
///     Covers ConfigFileLock and ConfigService.MutateAndSave. Every config.yaml mutator serializes the
///     whole settings graph, so two unsynchronized writers used to drop one another's change while both
///     reported success.
/// </summary>
[Collection("TendrilHome")]
public class ConfigFileLockTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-config-lock-test");
    private readonly string _originalTendrilHome;

    public ConfigFileLockTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        var yaml = @"
projects: []
verifications:
  - name: Alpha
    prompt: SEED-ALPHA
  - name: Beta
    prompt: SEED-BETA
";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), yaml);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
    }

    private string ConfigPath => Path.Combine(_tempDir.Path, "config.yaml");

    private static string PromptOf(TendrilSettings settings, string name) =>
        settings.Verifications.First(v => v.Name == name).Prompt;

    /// <summary>
    ///     The regression test. Before MutateAndSave existed, each writer loaded the file in its
    ///     ConfigService constructor, mutated one verification, and serialized the whole graph back, so
    ///     the later write reverted the earlier one to its seed value.
    /// </summary>
    [Fact]
    public void ConcurrentMutations_BothSurvive()
    {
        using var barrier = new Barrier(2);

        void Mutate(string name, string prompt)
        {
            var config = new ConfigService();
            barrier.SignalAndWait();
            config.MutateAndSave(s => s.Verifications.First(v => v.Name == name).Prompt = prompt);
        }

        var a = new Thread(() => Mutate("Alpha", "RACE-ALPHA"));
        var b = new Thread(() => Mutate("Beta", "RACE-BETA"));
        a.Start();
        b.Start();
        Assert.True(a.Join(TimeSpan.FromSeconds(30)));
        Assert.True(b.Join(TimeSpan.FromSeconds(30)));

        var reloaded = new ConfigService();
        Assert.Equal("RACE-ALPHA", PromptOf(reloaded.Settings, "Alpha"));
        Assert.Equal("RACE-BETA", PromptOf(reloaded.Settings, "Beta"));
    }

    [Fact]
    public void Acquire_SecondCallerBlocksThenSucceeds()
    {
        var acquired = new ManualResetEventSlim(false);
        var held = ConfigFileLock.Acquire(ConfigPath);

        var waiter = new Thread(() =>
        {
            using var _ = ConfigFileLock.Acquire(ConfigPath);
            acquired.Set();
        });
        waiter.Start();

        Assert.False(acquired.Wait(TimeSpan.FromMilliseconds(500)), "second Acquire completed while the lock was held");

        held.Dispose();

        Assert.True(acquired.Wait(TimeSpan.FromSeconds(10)), "second Acquire did not complete after release");
        Assert.True(waiter.Join(TimeSpan.FromSeconds(10)));
    }

    /// <summary>
    ///     The "fails loudly" half of the fix: the loser of a race throws instead of exiting 0 having
    ///     silently discarded the winner's write. Program.cs maps this to "Error: ..." and exit 1.
    /// </summary>
    [Fact]
    public void Acquire_TimesOutLoudly()
    {
        using var held = ConfigFileLock.Acquire(ConfigPath);

        TimeoutException? captured = null;
        var waiter = new Thread(() =>
        {
            captured = Assert.Throws<TimeoutException>(() => ConfigFileLock.Acquire(ConfigPath, 3, 10));
        });
        waiter.Start();
        Assert.True(waiter.Join(TimeSpan.FromSeconds(10)), "the timing-out Acquire never returned");

        Assert.NotNull(captured);
        Assert.Contains(ConfigPath + ".lock", captured!.Message);
    }

    [Fact]
    public void Acquire_EmptyConfigPath_ReturnsNoOp()
    {
        using var first = ConfigFileLock.Acquire("");
        // A no-op handle must not block a second acquisition, since the fresh-install path has no file to guard.
        using var second = ConfigFileLock.Acquire("");
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    /// <summary>
    ///     Proves the reload happens inside the lock: the callback sees a write committed after this
    ///     ConfigService was constructed, rather than mutating the stale graph it loaded at construction.
    /// </summary>
    [Fact]
    public void MutateAndSave_MutationSeesConcurrentWrite()
    {
        var config = new ConfigService();
        Assert.Equal("SEED-ALPHA", PromptOf(config.Settings, "Alpha"));

        // Out-of-band writer, as a separate service instance, lands between construction and mutation.
        var other = new ConfigService();
        other.MutateAndSave(s => s.Verifications.First(v => v.Name == "Alpha").Prompt = "OUT-OF-BAND");

        string? observed = null;
        config.MutateAndSave(s =>
        {
            observed = PromptOf(s, "Alpha");
            s.Verifications.First(v => v.Name == "Beta").Prompt = "LATER-BETA";
        });

        Assert.Equal("OUT-OF-BAND", observed);

        var reloaded = new ConfigService();
        Assert.Equal("OUT-OF-BAND", PromptOf(reloaded.Settings, "Alpha"));
        Assert.Equal("LATER-BETA", PromptOf(reloaded.Settings, "Beta"));
    }

    /// <summary>The lock file must not be mistaken for config, and must not survive the lock.</summary>
    [Fact]
    public void Acquire_LockFileIsDeletedOnRelease()
    {
        var lockPath = ConfigPath + ".lock";

        using (ConfigFileLock.Acquire(ConfigPath))
        {
            Assert.True(File.Exists(lockPath));
        }

        Assert.False(File.Exists(lockPath));
    }
}

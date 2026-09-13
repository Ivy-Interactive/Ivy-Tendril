using System.Runtime.CompilerServices;
using Ivy.Tendril.Helpers;
using CrashLog = Ivy.Helpers.CrashLog;

namespace Ivy.Tendril.Test;

/// <summary>
///     Pins the whole test assembly to a private, per-run Tendril home under the temp directory, so
///     that nothing the suite does can reach the developer's (or CI runner's) live Tendril home.
/// </summary>
/// <remarks>
///     <para>
///         This has to happen process-wide and before any test code runs, because
///         <see cref="CrashLog.Path" /> is a <c>Lazy&lt;string&gt;</c> built once from the process
///         <c>TENDRIL_HOME</c> environment variable and cached for the life of the process. A per-test
///         fixture is therefore too late: whichever test first raises an exception through
///         <c>JobLauncher</c> fixes the crash log location for every test after it. A
///         <see cref="ModuleInitializerAttribute" /> runs before the first access to anything in this
///         assembly, which is early enough.
///     </para>
///     <para>
///         The initializer also forces <see cref="CrashLog.Path" /> while the isolated home is
///         installed, so a later test that clears <c>TENDRIL_HOME</c> cannot move the crash log back
///         onto the live home, and asserts the resulting paths really are isolated. The assertion
///         throws, which fails the whole assembly loudly rather than silently polluting live state.
///     </para>
/// </remarks>
public static class TendrilHomeIsolation
{
    private static string? _root;

    /// <summary>The isolated Tendril home this test run is pinned to.</summary>
    public static string Root => _root ?? throw new InvalidOperationException(
        "TendrilHomeIsolation was never initialized; the module initializer should have run first.");

    /// <summary>The <c>Plans</c> directory inside <see cref="Root" />.</summary>
    public static string PlansRoot => Path.Combine(Root, "Plans");

    /// <summary>The <c>crash.log</c> path the process resolved at startup.</summary>
    public static string CrashLogPath { get; private set; } = "";

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_root is not null) return;

        var root = Path.Combine(Path.GetTempPath(), "ivy-tendril-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Plans"));
        _root = root;

        Apply();

        // Force CrashLog's cached Lazy<string> now, while the isolated home is installed.
        CrashLogPath = CrashLog.Path;

        AssertIsolated();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
    }

    /// <summary>
    ///     (Re-)installs the isolated home. A test that installs its own temporary home should call this
    ///     when tearing down, instead of restoring the <c>TENDRIL_HOME</c> value it captured or clearing
    ///     the variable: clearing it hands the rest of the run back to the machine's real home.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Isolation is installed at exactly one layer: the process <c>TENDRIL_HOME</c> variable.
    ///         That is the lowest layer that still shadows every source of live state
    ///         <see cref="PathHelper.GetDefaultTendrilHome" /> would otherwise fall through to (the
    ///         User-scope <c>TENDRIL_HOME</c> variable and the <c>~/.tendril_location</c> pointer file).
    ///     </para>
    ///     <para>
    ///         <see cref="PathHelper.DefaultTendrilHomeOverride" /> is deliberately left <c>null</c>.
    ///         It outranks <c>TENDRIL_HOME</c>, so pinning it assembly-wide would not just isolate the
    ///         suite from the live home, it would also outrank the per-test temporary homes that most
    ///         tests install through <c>TENDRIL_HOME</c>, and they would all read and write the shared
    ///         root instead. The highest-priority hook has to stay free for per-test use.
    ///     </para>
    ///     <para>
    ///         <c>TENDRIL_PLANS</c> is cleared for the same reason, in the other direction: a developer
    ///         shell usually exports it pointing at the live <c>Plans</c> directory, and
    ///         <see cref="PlanCommandHelpers.GetPlansDirectory" /> prefers it over the resolved home, so
    ///         it must not be inherited. Pinning it to <see cref="PlansRoot" /> would again outrank the
    ///         per-test homes. Clearing it makes the resolved home the single source of truth.
    ///     </para>
    /// </remarks>
    public static void Apply()
    {
        PathHelper.DefaultTendrilHomeOverride = null;
        Environment.SetEnvironmentVariable("TENDRIL_HOME", Root);
        Environment.SetEnvironmentVariable("TENDRIL_PLANS", null);
    }

    /// <summary>
    ///     The Tendril homes configured on this machine, from the sources
    ///     <see cref="PathHelper.GetDefaultTendrilHome" /> consults after the process environment:
    ///     the User-scope <c>TENDRIL_HOME</c> variable and the <c>~/.tendril_location</c> pointer file.
    /// </summary>
    public static IReadOnlyList<string> GetMachineTendrilHomes()
    {
        var homes = new List<string>();

        if (OperatingSystem.IsWindows())
            try
            {
                var userHome = Environment.GetEnvironmentVariable("TENDRIL_HOME", EnvironmentVariableTarget.User)?.Trim();
                if (!string.IsNullOrEmpty(userHome))
                {
                    if (userHome.StartsWith('"') && userHome.EndsWith('"'))
                        userHome = userHome[1..^1];
                    homes.Add(userHome);
                }
            }
            catch
            {
                /* reading User-scope variables can be denied; not a reason to fail */
            }

        try
        {
            var pointerFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril_location");
            if (File.Exists(pointerFile))
            {
                var location = File.ReadAllText(pointerFile).Trim();
                if (!string.IsNullOrEmpty(location)) homes.Add(location);
            }
        }
        catch
        {
            /* best effort */
        }

        return homes
            .Select(NormalizePath)
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Whether <paramref name="path" /> is <see cref="Root" /> or lives under it.</summary>
    public static bool IsInsideRoot(string? path) => IsInside(path, Root);

    /// <summary>Whether <paramref name="path" /> is <paramref name="directory" /> or lives under it.</summary>
    public static bool IsInside(string? path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = NormalizePath(path);
        var parent = NormalizePath(directory);
        return normalized.Equals(parent, StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertIsolated()
    {
        var resolvedHome = PathHelper.GetDefaultTendrilHome();
        var crashLogDir = Path.GetDirectoryName(CrashLogPath) ?? "";

        // Effect-level checks: it is not enough that the override is set, the values the suite will
        // actually write through have to land inside the isolated root.
        if (!IsInsideRoot(resolvedHome))
            throw new InvalidOperationException(Message(
                $"PathHelper.GetDefaultTendrilHome() resolved to '{resolvedHome}'"));

        if (!IsInsideRoot(crashLogDir))
            throw new InvalidOperationException(Message(
                $"Ivy.Helpers.CrashLog resolved to '{CrashLogPath}'"));

        // Named explicitly so the failure message says which live home was about to be written to.
        foreach (var machineHome in GetMachineTendrilHomes())
        {
            if (IsInside(resolvedHome, machineHome))
                throw new InvalidOperationException(Message(
                    $"PathHelper.GetDefaultTendrilHome() resolved to the machine's live Tendril home '{machineHome}'"));

            if (IsInside(crashLogDir, machineHome))
                throw new InvalidOperationException(Message(
                    $"Ivy.Helpers.CrashLog resolved into the machine's live Tendril home '{machineHome}'"));
        }

        string Message(string detail) =>
            $"Tendril home isolation failed: {detail}, which is not inside the test root '{Root}'. " +
            "The test suite must never resolve the machine's real Tendril home - it would write " +
            "crash.log entries and plan state into live data. See TendrilHomeIsolation.";
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return path.Trim();
        }
    }

    private static void Cleanup()
    {
        var root = _root;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                try
                {
                    var attrs = File.GetAttributes(file);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                }
                catch
                {
                    /* best effort */
                }
        }
        catch
        {
            /* best effort */
        }

        try
        {
            Directory.Delete(root, true);
        }
        catch
        {
            /* best effort cleanup; a leftover temp directory is harmless */
        }
    }
}

namespace Ivy.Tendril.Helpers;

public static class TendrilInstallHelper
{
    /// <summary>
    /// Overrides the user profile root used by all path builders below, for tests. Mirrors
    /// <see cref="PathHelper.DefaultTendrilHomeOverride"/>.
    /// </summary>
    internal static string? UserProfileOverride { get; set; }

    private static string UserProfile =>
        UserProfileOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string LegacyToolsDir => Path.Combine(UserProfile, ".dotnet", "tools");

    private static string LegacyStoreDir => Path.Combine(LegacyToolsDir, ".store", "ivy.tendril");

    private static string LegacyToolExe => Path.Combine(LegacyToolsDir, OperatingSystem.IsWindows() ? "tendril.exe" : "tendril");

    public static bool IsLegacyDotnetToolProcess()
    {
        var baseDir = Path.TrimEndingDirectorySeparator(System.AppContext.BaseDirectory);
        var toolsDir = Path.TrimEndingDirectorySeparator(LegacyToolsDir);
        return baseDir.Equals(toolsDir, PathComparison) ||
               baseDir.StartsWith(toolsDir + Path.DirectorySeparatorChar, PathComparison);
    }

    public static string? GetLegacyToolVersion()
    {
        if (!Directory.Exists(LegacyStoreDir)) return null;

        try
        {
            return Directory.GetDirectories(LegacyStoreDir)
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderByDescending(name => Version.TryParse(name, out var v) ? v : new Version(0, 0, 0, 0))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static bool IsLegacyToolInstalled() => FindLegacyToolInstallPath() != null;

    internal static string? FindLegacyToolInstallPath()
    {
        if (Directory.Exists(LegacyStoreDir)) return LegacyStoreDir;
        if (File.Exists(LegacyToolExe)) return LegacyToolExe;
        return null;
    }

    public static string? FindInstalledCli()
    {
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(localAppData, "IvyTendril", "current", "Ivy.Tendril.exe"));
            candidates.Add(Path.Combine(localAppData, "IvyTendril", "Ivy.Tendril.exe"));
        }
        else
        {
            if (OperatingSystem.IsMacOS())
            {
                candidates.Add("/Applications/Ivy Tendril.app/Contents/MacOS/Ivy.Tendril");
                candidates.Add(Path.Combine(UserProfile, "Applications", "Ivy Tendril.app", "Contents", "MacOS", "Ivy.Tendril"));
            }

            candidates.Add("/usr/local/bin/tendril");
            candidates.Add(Path.Combine(UserProfile, ".local", "bin", "tendril"));
        }

        var legacyToolsDir = Path.TrimEndingDirectorySeparator(LegacyToolsDir);

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;

            var resolvedTarget = candidate;
            try
            {
                var target = File.ResolveLinkTarget(candidate, returnFinalTarget: true);
                if (target != null) resolvedTarget = target.FullName;
            }
            catch
            {
                // Not a symlink, or link resolution failed; treat the candidate path as-is.
            }

            if (resolvedTarget.Equals(legacyToolsDir, PathComparison) ||
                resolvedTarget.StartsWith(legacyToolsDir + Path.DirectorySeparatorChar, PathComparison))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    public static string? ResolveOnPath(string command)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var dirs = pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        var names = OperatingSystem.IsWindows()
            ? new[] { command + ".exe", command + ".cmd", command }
            : new[] { command };

        foreach (var dir in dirs)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}

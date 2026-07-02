using System.Runtime.InteropServices;

namespace Ivy.Tendril.Agents.Helpers;

/// <summary>
/// Cross-platform binary resolution. Finds executables on PATH with platform-appropriate extensions.
/// </summary>
public static class BinaryResolver
{
    private static readonly string[] WindowsExtensions = [".cmd", ".exe", ".bat"];

    public static string? FindOnPath(string commandName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var dirs = pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in dirs)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                foreach (var ext in WindowsExtensions)
                {
                    var candidate = Path.Combine(dir, commandName + ext);
                    if (File.Exists(candidate)) return candidate;
                }

                var plain = Path.Combine(dir, commandName);
                if (File.Exists(plain)) return plain;
            }
            else
            {
                var candidate = Path.Combine(dir, commandName);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    public static bool IsInstalled(string commandName) => FindOnPath(commandName) is not null;
}

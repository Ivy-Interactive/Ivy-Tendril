using System.Runtime.InteropServices;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.OpenCode;

internal static class OpenCodeBinaryResolver
{
    private static string? _cachedPath;

    public static string Resolve()
    {
        if (_cachedPath != null) return _cachedPath;

        // 1. Check PATH
        var path = BinaryResolver.FindOnPath("opencode");
        if (path != null)
        {
            return _cachedPath = path;
        }

        // 2. Check user profile directory ~/.opencode/bin/opencode
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fallbackDir = Path.Combine(home, ".opencode", "bin");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] extensions = [".cmd", ".exe", ".bat"];
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(fallbackDir, "opencode" + ext);
                if (File.Exists(candidate))
                {
                    return _cachedPath = candidate;
                }
            }
        }
        else
        {
            var candidate = Path.Combine(fallbackDir, "opencode");
            if (File.Exists(candidate))
            {
                return _cachedPath = candidate;
            }
        }

        // Fallback to default name if not found anywhere
        return _cachedPath = "opencode";
    }
}

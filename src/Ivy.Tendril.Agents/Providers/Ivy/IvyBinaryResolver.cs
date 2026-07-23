using System.Runtime.InteropServices;
using Ivy.Tendril.Agents.Helpers;

namespace Ivy.Tendril.Agents.Providers.Ivy;

internal static class IvyBinaryResolver
{
    private static string? _cachedPath;

    public static string Resolve()
    {
        if (_cachedPath != null) return _cachedPath;

        // 1. Check PATH
        var path = BinaryResolver.FindOnPath("ivy-agent");
        if (path != null)
        {
            return _cachedPath = path;
        }

        // 2. Check user profile directory ~/.ivy-agent/bin/ivy-agent
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fallbackDir = Path.Combine(home, ".ivy-agent", "bin");
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] extensions = [".cmd", ".exe", ".bat"];
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(fallbackDir, "ivy-agent" + ext);
                if (File.Exists(candidate))
                {
                    return _cachedPath = candidate;
                }
            }
        }
        else
        {
            var candidate = Path.Combine(fallbackDir, "ivy-agent");
            if (File.Exists(candidate))
            {
                return _cachedPath = candidate;
            }
        }

        // Fallback to default name if not found anywhere
        return _cachedPath = "ivy-agent";
    }
}

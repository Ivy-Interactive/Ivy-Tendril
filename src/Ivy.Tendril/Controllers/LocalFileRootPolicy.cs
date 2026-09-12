using System.Security;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Controllers;

/// <summary>
/// Confines GET /ivy/local-file to a set of configured roots. Unlike a general-purpose "no
/// restriction when unset" policy, an empty root list here means deny everything: fail closed.
/// </summary>
internal static class LocalFileRootPolicy
{
    public static List<string> ComputeRoots(IConfigService config)
    {
        var candidates = new List<string?> { config.TendrilHome, config.PlanFolder };

        foreach (var project in config.Projects)
        {
            candidates.AddRange(project.RepoPaths);
        }

        if (config.Settings.Security?.LocalFileRoots is { } extraRoots)
        {
            candidates.AddRange(extraRoots);
        }

        var roots = new List<string>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string normalized;
            try
            {
                normalized = PathHelper.ResolvePath(candidate)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
            {
                continue;
            }

            if (normalized.Length > 0 && !roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(normalized);
            }
        }

        return roots;
    }

    public static bool TryResolve(string? path, IReadOnlyList<string> roots, out string fullPath)
    {
        fullPath = "";

        if (string.IsNullOrEmpty(path) || roots.Count == 0)
        {
            return false;
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var target = resolved;
        try
        {
            var linkTarget = new FileInfo(resolved).ResolveLinkTarget(returnFinalTarget: true);
            if (linkTarget != null)
            {
                target = linkTarget.FullName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException)
        {
            // Leave target as the unresolved path; containment is still checked below.
        }

        foreach (var root in roots)
        {
            if (IsWithinRoot(target, root))
            {
                fullPath = resolved;
                return true;
            }
        }

        return false;
    }

    private static bool IsWithinRoot(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

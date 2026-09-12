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
    private const int MaxLinkHops = 40;

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

            // A root can itself sit behind a symlink (e.g. macOS's /var -> /private/var), so a
            // request resolved to its real path would otherwise never match. Keep both forms.
            if (normalized.Length > 0 && TryResolveRealPath(normalized, out var resolvedRoot))
            {
                resolvedRoot = resolvedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (resolvedRoot.Length > 0 && !roots.Contains(resolvedRoot, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(resolvedRoot);
                }
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

        if (!TryResolveRealPath(resolved, out var target))
        {
            return false;
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

    /// <summary>
    /// realpath-style resolution: walks every path segment (not just the final one) and resolves
    /// each symlink it finds, so a symlinked directory in the middle of the path is followed too.
    /// Resolves one hop at a time (<c>returnFinalTarget: false</c>) and tracks hops itself rather
    /// than delegating multi-hop resolution to <see cref="FileSystemInfo.ResolveLinkTarget"/>'s own
    /// <c>true</c> mode: that mode throws on a symlink cycle instead of returning, which would make
    /// the caller fall back to the unresolved (still-contained-looking) input path. Counting hops
    /// ourselves lets a cycle be rejected outright instead of silently passing containment.
    /// Internal (rather than private) so tests can canonicalize their own fixture paths the same
    /// way, since ambient test roots (e.g. the OS temp dir) can themselves sit behind a symlink.
    /// </summary>
    internal static bool TryResolveRealPath(string fullPath, out string realPath)
    {
        var pathRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
        var pending = new Queue<string>(SplitComponents(fullPath, pathRoot.Length));
        var root = pathRoot;
        var accepted = new List<string>();
        var hops = 0;

        while (pending.Count > 0)
        {
            var component = pending.Dequeue();

            if (component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                if (accepted.Count > 0)
                {
                    accepted.RemoveAt(accepted.Count - 1);
                }
                continue;
            }

            var candidate = BuildPath(root, accepted.Append(component));

            FileSystemInfo? linkTarget;
            try
            {
                linkTarget = new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException)
            {
                // Missing or unreadable component: it cannot be a symlink, so keep walking and let
                // containment decide (preserves today's behaviour for e.g. a deleted screenshot).
                linkTarget = null;
            }

            if (linkTarget == null)
            {
                accepted.Add(component);
                continue;
            }

            hops++;
            if (hops > MaxLinkHops)
            {
                realPath = "";
                return false;
            }

            var targetPath = linkTarget.FullName;
            var targetRoot = Path.GetPathRoot(targetPath) ?? string.Empty;
            var requeued = new Queue<string>(SplitComponents(targetPath, targetRoot.Length));
            foreach (var remaining in pending)
            {
                requeued.Enqueue(remaining);
            }

            root = targetRoot;
            accepted = [];
            pending = requeued;
        }

        realPath = BuildPath(root, accepted);
        return true;
    }

    private static IEnumerable<string> SplitComponents(string path, int rootLength)
    {
        return path
            .Substring(rootLength)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
    }

    private static string BuildPath(string root, IEnumerable<string> components)
    {
        var list = components as ICollection<string> ?? components.ToList();
        if (list.Count == 0)
        {
            return root;
        }

        var needsSeparator = root.Length == 0
            || (root[^1] != Path.DirectorySeparatorChar && root[^1] != Path.AltDirectorySeparatorChar);

        return needsSeparator
            ? root + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, list)
            : root + string.Join(Path.DirectorySeparatorChar, list);
    }
}

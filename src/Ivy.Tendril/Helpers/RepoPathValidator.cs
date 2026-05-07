using System.Text.RegularExpressions;

namespace Ivy.Tendril.Helpers;

public enum RepoPathKind { SshUrl, HttpUrl, LocalPath, Invalid }

public static class RepoPathValidator
{
    // git@<host>:<owner>/<repo>(.git)?
    private static readonly Regex SshPattern = new(
        @"^git@[\w.\-]+:[\w.\-]+/[\w.\-]+(?:\.git)?$",
        RegexOptions.Compiled);

    // http(s)://<host>/<path>(.git)?
    private static readonly Regex HttpPattern = new(
        @"^https?://[\w.\-]+(:\d+)?(/[\w.\-~%]+)+(?:\.git)?$",
        RegexOptions.Compiled);

    public static RepoPathKind Classify(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return RepoPathKind.Invalid;

        input = input.Trim();

        if (IsSshUrl(input)) return RepoPathKind.SshUrl;
        if (IsHttpUrl(input)) return RepoPathKind.HttpUrl;
        if (IsLocalPath(input)) return RepoPathKind.LocalPath;

        return RepoPathKind.Invalid;
    }

    public static bool IsValid(string input) => Classify(input) != RepoPathKind.Invalid;

    public static bool IsSshUrl(string input)
        => !string.IsNullOrWhiteSpace(input) && SshPattern.IsMatch(input.Trim());

    public static bool IsHttpUrl(string input)
        => !string.IsNullOrWhiteSpace(input) && HttpPattern.IsMatch(input.Trim());

    public static bool IsLocalPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.Trim();

        // Absolute Linux/macOS path
        if (trimmed.StartsWith('/')) return true;
        // Home-relative path
        if (trimmed.StartsWith('~')) return true;
        // Windows drive letter (e.g. C:\...)
        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':') return true;

        return false;
    }

    public static string? ExtractRepoName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        var kind = Classify(input);

        switch (kind)
        {
            case RepoPathKind.SshUrl:
                {
                    // git@host:owner/repo.git -> repo
                    var colonIdx = input.IndexOf(':');
                    if (colonIdx < 0) return null;
                    var path = input[(colonIdx + 1)..];
                    if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                        path = path[..^4];
                    var parts = path.Split('/');
                    return parts.Length > 0 ? parts[^1] : null;
                }
            case RepoPathKind.HttpUrl:
                {
                    var trimmed = input;
                    if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                        trimmed = trimmed[..^4];
                    var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    return parts.Length > 0 ? parts[^1] : null;
                }
            case RepoPathKind.LocalPath:
                {
                    var trimmed = input.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return Path.GetFileName(trimmed);
                }
            default:
                return null;
        }
    }
}

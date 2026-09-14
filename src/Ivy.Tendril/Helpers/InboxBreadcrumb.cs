using System.Security.Cryptography;
using System.Text;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Naming and frontmatter handling for the crash-recovery breadcrumbs in
///     <c>TENDRIL_HOME/Inbox</c>. A breadcrumb used to be named after the job that wrote it, so one
///     logical task accumulated one file per submission and every restart turned each of them into a
///     fresh job (#2710). The name is now derived from the task itself, so resubmitting the same
///     description into the same project reuses the same file.
/// </summary>
public static class InboxBreadcrumb
{
    /// <summary>How many times a single breadcrumb may be resurrected before it is dead-lettered.</summary>
    public const int RecoveryAttemptCap = 2;

    private const string RecoveryAttemptsKey = "recoveryAttempts";

    /// <summary>
    ///     Collapses the incidental differences between two spellings of the same request: trims,
    ///     reduces every run of whitespace to a single space, and lowercases.
    /// </summary>
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    /// <summary>
    ///     A stable 16 hex character key for a (project, description) pair. Shared with the dedup
    ///     paths so they agree on what counts as the same task.
    /// </summary>
    public static string TaskHash(string? project, string? description)
    {
        var key = (project ?? "").Trim().ToLowerInvariant() + "\n" + Normalise(description);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexStringLower(hash)[..16];
    }

    /// <summary>The breadcrumb file name for a task, including the <c>.md.processing</c> suffix.</summary>
    public static string FileName(string? project, string? description)
        => $"pending-{TaskHash(project, description)}.md.processing";

    /// <summary>
    ///     Reads the recovery counter out of a breadcrumb's frontmatter. Missing frontmatter, a
    ///     missing key and a malformed value all read as 0: an unreadable counter must not look like
    ///     an exhausted one.
    /// </summary>
    public static int ReadRecoveryAttempts(string? content)
    {
        if (!TrySplit(content, out var frontmatter, out _))
            return 0;

        foreach (var line in frontmatter.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(RecoveryAttemptsKey + ":", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = trimmed[(RecoveryAttemptsKey.Length + 1)..].Trim();
            return int.TryParse(value, out var attempts) && attempts > 0 ? attempts : 0;
        }

        return 0;
    }

    /// <summary>
    ///     Rewrites the recovery counter in place, leaving every other frontmatter key (<c>project</c>,
    ///     <c>sourcePath</c>) and the body untouched. Content with no frontmatter gains one.
    /// </summary>
    public static string WithRecoveryAttempts(string? content, int attempts)
    {
        var line = $"{RecoveryAttemptsKey}: {attempts}";

        if (!TrySplit(content, out var frontmatter, out var body))
            return $"---\n{line}\n---\n{(content ?? "").TrimStart('\n', '\r')}";

        var lines = frontmatter.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var index = lines.FindIndex(l =>
            l.Trim().StartsWith(RecoveryAttemptsKey + ":", StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            lines[index] = line;
        else
            lines.Add(line);

        return $"---\n{string.Join('\n', lines)}\n---\n{body}";
    }

    /// <summary>
    ///     Splits frontmatter from body the same way <c>InboxWatcherService.ParseContent</c> does, so
    ///     the two never disagree about where the frontmatter ends.
    /// </summary>
    private static bool TrySplit(string? content, out string frontmatter, out string body)
    {
        frontmatter = "";
        body = "";

        if (content == null || !content.StartsWith("---"))
            return false;

        var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIndex <= 3)
            return false;

        frontmatter = content.Substring(3, endIndex - 3).Trim();
        body = content[(endIndex + 3)..].Trim();
        return true;
    }
}

using System.Security.Cryptography;
using System.Text;
using Ivy.Plugins.Inbox;

namespace Ivy.Tendril.Services;

internal class PluginInbox(string inboxPath) : IInbox
{
    public void Add(string description)
    {
        Add(new InboxItem { Description = description });
    }

    public void Add(InboxItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Description, nameof(item.Description));
        var filename = GenerateFilename(item);
        var content = FormatContent(item);
        Directory.CreateDirectory(inboxPath);
        File.WriteAllText(Path.Combine(inboxPath, filename), content);
    }

    public void AddRange(IEnumerable<InboxItem> items)
    {
        foreach (var item in items)
            Add(item);
    }

    internal static string GenerateFilename(InboxItem item)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss");
        var identifier = !string.IsNullOrWhiteSpace(item.SourceIdentifier)
            ? SanitizeForFilename(item.SourceIdentifier)
            : ShortHash(item.Description);
        return $"{timestamp}-{identifier}.md";
    }

    internal static string FormatContent(InboxItem item)
    {
        var sb = new StringBuilder();
        var hasFrontmatter = item.Project != "Auto"
            || item.SourceUrl != null
            || item.SourceIdentifier != null
            || item.Labels.Count > 0;

        if (hasFrontmatter)
        {
            sb.AppendLine("---");
            if (item.Project != "Auto")
                sb.AppendLine($"project: {item.Project}");
            if (item.SourceUrl != null)
                sb.AppendLine($"sourceUrl: {item.SourceUrl}");
            if (item.SourceIdentifier != null)
                sb.AppendLine($"sourceIdentifier: {item.SourceIdentifier}");
            if (item.Labels.Count > 0)
                sb.AppendLine($"labels: [{string.Join(", ", item.Labels)}]");
            sb.AppendLine("---");
        }

        sb.Append(item.Description);
        return sb.ToString();
    }

    private static string SanitizeForFilename(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : "item";
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }
}

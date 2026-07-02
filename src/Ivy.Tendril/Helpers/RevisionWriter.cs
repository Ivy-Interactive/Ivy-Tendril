using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Single entry point for writing a new plan revision file. Resolves the next
///     <c>NNN</c>, polishes the markdown (file-link repair, <c>plan://</c> conversion, …)
///     via <see cref="IConfigService.PolishMarkdown" />, and writes <c>Revisions/NNN.md</c>.
///     Every revision-write surface (CLI, MCP, REST, in-process service) funnels through here
///     so agent-produced markdown is polished and persisted consistently.
/// </summary>
public static class RevisionWriter
{
    /// <summary>
    ///     Writes <paramref name="content" /> as the next revision under
    ///     <paramref name="planFolder" />, returning the full path written.
    /// </summary>
    public static string WriteNext(string planFolder, string content, IConfigService config)
    {
        var revisionsDir = Path.Combine(planFolder, "Revisions");
        FileHelper.EnsureDirectory(revisionsDir);

        var filePath = Path.Combine(revisionsDir, $"{NextRevisionNumber(revisionsDir):D3}.md");
        FileHelper.WriteAllText(filePath, config.PolishMarkdown(content));
        return filePath;
    }

    /// <summary>Highest existing <c>NNN.md</c> in <paramref name="revisionsDir" /> plus one (1 if none).</summary>
    public static int NextRevisionNumber(string revisionsDir)
    {
        var max = 0;
        foreach (var file in Directory.GetFiles(revisionsDir, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(name, out var num) && num > max)
                max = num;
        }

        return max + 1;
    }
}

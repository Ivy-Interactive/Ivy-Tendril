namespace Ivy.Tendril.Helpers;

public static class PromptwareMemoryResolver
{
    public static string NormalizeName(string filename)
    {
        var trimmed = filename.Trim();

        if (trimmed.StartsWith("[[") && trimmed.EndsWith("]]") && trimmed.Length >= 4)
            trimmed = trimmed[2..^2].Trim();

        var name = Path.GetFileName(trimmed);

        if (string.IsNullOrEmpty(Path.GetExtension(name)))
            name += ".md";

        return name;
    }

    public static string? Resolve(string memoryDir, string filename)
    {
        var normalized = NormalizeName(filename);
        var raw = Path.GetFileName(filename);

        var normalizedPath = Path.Combine(memoryDir, normalized);
        if (File.Exists(normalizedPath))
            return normalizedPath;

        if (!string.IsNullOrEmpty(raw) && raw != normalized)
        {
            var rawPath = Path.Combine(memoryDir, raw);
            if (File.Exists(rawPath))
                return rawPath;
        }

        if (!Directory.Exists(memoryDir))
            return null;

        foreach (var file in Directory.EnumerateFiles(memoryDir))
        {
            var candidateName = Path.GetFileName(file);
            if (string.Equals(candidateName, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidateName, raw, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    public static IReadOnlyList<string> Suggest(string memoryDir, string filename, int max = 3)
    {
        if (!Directory.Exists(memoryDir))
            return [];

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(memoryDir)
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrEmpty(f) && !f!.StartsWith('.'))
                .Select(f => f!)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        var stem = Path.GetFileNameWithoutExtension(NormalizeName(filename));
        if (string.IsNullOrEmpty(stem))
            return [];

        return files
            .Select(f => (Name: f, Score: ScoreMatch(f, stem)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name.Length)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Take(max)
            .Select(x => x.Name)
            .ToList();
    }

    private static int ScoreMatch(string candidateFileName, string stem)
    {
        var candidateStem = Path.GetFileNameWithoutExtension(candidateFileName);
        if (string.IsNullOrEmpty(candidateStem))
            return 0;

        if (candidateStem.Contains(stem, StringComparison.OrdinalIgnoreCase) ||
            stem.Contains(candidateStem, StringComparison.OrdinalIgnoreCase))
            return 1000 - Math.Abs(candidateStem.Length - stem.Length);

        var stemTokens = stem.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var candidateTokens = candidateStem.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return stemTokens.Count(t => candidateTokens.Contains(t, StringComparer.OrdinalIgnoreCase));
    }
}

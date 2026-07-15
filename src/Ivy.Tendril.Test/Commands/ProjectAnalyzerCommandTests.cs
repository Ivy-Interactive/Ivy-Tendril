using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Test.Commands;

public class ProjectAnalyzerCommandTests
{
    private static string WriteFile(string dir, string relativePath, string content)
    {
        var full = Path.Combine(dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public async Task AnalyzeToYaml_DetectsNodeStack_AndEmitsTrimmedReport()
    {
        using var tempDir = new TempDirectoryFixture("tendril-analyzer-test");

        WriteFile(tempDir.Path, "package.json", """
            {
              "name": "demo-app",
              "dependencies": { "react": "^18.2.0", "react-dom": "^18.2.0" },
              "devDependencies": { "vite": "^5.0.0", "typescript": "^5.4.0", "vitest": "^1.4.0" }
            }
            """);
        WriteFile(tempDir.Path, "tsconfig.json", "{ \"compilerOptions\": { \"strict\": true } }");
        WriteFile(tempDir.Path, "src/main.tsx", "export const App = () => null;\n");

        var yaml = await ProjectAnalyzerCommand.AnalyzeToYamlAsync(tempDir.Path);

        Assert.False(string.IsNullOrWhiteSpace(yaml));

        // Well-formed and round-trips through the deserializer.
        var parsed = YamlHelper.Deserializer.Deserialize<Dictionary<string, object>>(yaml);
        Assert.Contains("components", parsed.Keys);

        // React is a defining framework detected from the dependency — must appear.
        Assert.Contains("React", yaml);

        // Incidental/noise fields from the full analyzer report must NOT be in the trimmed output.
        Assert.DoesNotContain("manifests", yaml);
        Assert.DoesNotContain("sizeBytes", yaml);
        Assert.DoesNotContain("metadata", yaml);
        Assert.DoesNotContain("evidence", yaml);

        // Low-confidence signals are filtered out, so confidence values are only medium/high.
        Assert.DoesNotContain("confidence: low", yaml);
    }

    [Fact]
    public async Task AnalyzeToYaml_EmptyFolder_ReturnsWellFormedYaml()
    {
        using var tempDir = new TempDirectoryFixture("tendril-analyzer-empty");

        var yaml = await ProjectAnalyzerCommand.AnalyzeToYamlAsync(tempDir.Path);

        // No throw, and the result is valid YAML (possibly just "{}").
        var parsed = YamlHelper.Deserializer.Deserialize<Dictionary<string, object>>(yaml ?? "");
        Assert.NotNull(parsed);
    }
}

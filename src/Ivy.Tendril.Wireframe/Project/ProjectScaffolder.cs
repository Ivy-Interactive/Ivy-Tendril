using System.Text.Json;
using Ivy.Tendril.Wireframe.Assets;

namespace Ivy.Tendril.Wireframe.Project;

public sealed record ScaffoldResult(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Skipped,
    int TypeFiles);

/// <summary>
/// Creates and refreshes a wireframe project.
///
/// Two rules, applied strictly:
///   * User files are never clobbered. Re-running `setup` on an existing project reports
///     what it skipped rather than overwriting the agent's work.
///   * .wireframe/ is always clobbered. It holds nothing user-authored, so regenerating it
///     is how a tool upgrade picks up new types and templates.
///
/// Nothing git-related is written. These projects are throwaway mockups, so a .gitignore
/// and .gitkeep placeholders were noise. If you do commit one, add `.wireframe/` to your
/// own ignore file -- it is a few MB of regenerable type definitions.
/// </summary>
public sealed class ProjectScaffolder(AssetCatalog assets)
{
    public ScaffoldResult Scaffold(WireframeProject project)
    {
        var created = new List<string>();
        var skipped = new List<string>();

        Directory.CreateDirectory(project.Root);
        Directory.CreateDirectory(project.SourceDir);
        Directory.CreateDirectory(project.ScreenshotsDir);

        MigrateAppIntoSource(project);

        WriteIfAbsent(project, project.IndexHtml,
            ScaffoldTemplates.IndexHtml.Replace("{{TITLE}}", project.Name), created, skipped);
        WriteIfAbsent(project, project.EntryPoint, ScaffoldTemplates.MainTsx, created, skipped);
        WriteIfAbsent(project, Path.Combine(project.SourceDir, "App.tsx"),
            ScaffoldTemplates.AppTsx, created, skipped);
        WriteIfAbsent(project, Path.Combine(project.SourceDir, "wireframe-ready.ts"),
            ScaffoldTemplates.WireframeReadyTs, created, skipped);
        WriteIfAbsent(project, project.TsConfig, ScaffoldTemplates.TsConfig, created, skipped);

        var typeFiles = MaterializeWorkspace(project);
        return new ScaffoldResult(created, skipped, typeFiles);
    }

    /// <summary>
    /// Moves index.html and public/ into src/ for projects scaffolded before the app lived
    /// entirely under src/.
    ///
    /// Done by moving rather than rewriting: the user may have edited index.html, and
    /// regenerating it would throw that away. If both locations somehow exist, the one
    /// already in src/ wins and the stray copy is left alone for the user to delete.
    /// </summary>
    private static void MigrateAppIntoSource(WireframeProject project)
    {
        try
        {
            if (File.Exists(project.LegacyIndexHtml) && !File.Exists(project.IndexHtml))
                File.Move(project.LegacyIndexHtml, project.IndexHtml);

            if (Directory.Exists(project.LegacyPublicDir))
            {
                foreach (var source in Directory.EnumerateFiles(
                             project.LegacyPublicDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(project.LegacyPublicDir, source);
                    var destination = Path.Combine(project.PublicDir, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (!File.Exists(destination)) File.Move(source, destination);
                }

                // Only remove it once it is genuinely empty, so nothing is ever discarded.
                if (!Directory.EnumerateFileSystemEntries(project.LegacyPublicDir).Any())
                    Directory.Delete(project.LegacyPublicDir);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked file just means the old copy stays put; the project still works
            // because the scaffold writes a fresh index.html into src/ if none is there.
        }
    }

    /// <summary>
    /// Rewrites .wireframe/ from the embedded payload. Cheap enough (a few MB of .d.ts) to
    /// do unconditionally when the stamp does not match.
    /// </summary>
    public int MaterializeWorkspace(WireframeProject project)
    {
        if (Directory.Exists(project.WorkDir))
            Directory.Delete(project.WorkDir, recursive: true);

        Directory.CreateDirectory(project.WorkDir);
        var count = assets.ExtractTo("types", project.TypesDir);

        File.WriteAllText(
            Path.Combine(project.WorkDir, "tsconfig.base.json"),
            ScaffoldTemplates.TsConfigBase.Replace("{{PATHS}}", BuildTsPaths(project)));

        WriteStamp(project);
        return count;
    }

    /// <summary>True when .wireframe/ was built by a different tool version or payload.</summary>
    public bool NeedsRefresh(WireframeProject project)
    {
        if (!File.Exists(project.StampFile)) return true;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(project.StampFile));
            var root = doc.RootElement;
            return root.GetProperty("toolVersion").GetString() != AssetCatalog.ToolVersion
                || root.GetProperty("artifactsHash").GetString() != assets.Hash;
        }
        catch
        {
            return true;
        }
    }

    private void WriteStamp(WireframeProject project) =>
        File.WriteAllText(project.StampFile, JsonSerializer.Serialize(new
        {
            toolVersion = AssetCatalog.ToolVersion,
            artifactsHash = assets.Hash,
            generated = DateTimeOffset.UtcNow,
            note = "Regenerated by `tendril wireframe setup`/`serve`. Safe to delete.",
        }, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>
    /// Maps every import-map specifier to its vendored .d.ts, so the editor resolves the
    /// same names the browser does.
    /// </summary>
    private string BuildTsPaths(WireframeProject project)
    {
        var manifest = VendorManifest.Load(assets);
        var entries = new List<string>();

        void Add(string specifier, string target) =>
            entries.Add($"      \"{specifier}\": [\"{target}\"]");

        foreach (var specifier in manifest.Specifiers.Keys.OrderBy(k => k))
        {
            var target = specifier switch
            {
                "react" => ".wireframe/types/react",
                "react/jsx-runtime" => ".wireframe/types/react/jsx-runtime",
                "react-dom" => ".wireframe/types/react-dom",
                "react-dom/client" => ".wireframe/types/react-dom/client",
                "tendril-wireframes" => ".wireframe/types/tendril-wireframes/index.d.ts",
                "lucide-react" => ".wireframe/types/lucide-react/index.d.ts",
                _ => $".wireframe/types/{specifier}",
            };

            // Only map specifiers we actually vendored types for; an unresolvable path
            // entry is worse than none, because it silences the "cannot find module" hint.
            var probe = Path.Combine(project.Root, target.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(probe) || Directory.Exists(probe) || File.Exists(probe + ".d.ts"))
                Add(specifier, target);
        }

        Add("csstype", ".wireframe/types/csstype");
        return string.Join(",\n", entries);
    }

    private static void WriteIfAbsent(
        WireframeProject project, string path, string content,
        List<string> created, List<string> skipped)
    {
        var relative = project.RelativePath(path);
        if (File.Exists(path)) { skipped.Add(relative); return; }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        created.Add(relative);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Ivy.Tendril.Wireframe.Project;

/// <summary>
/// The on-disk layout of a wireframe project.
///
/// <code>
/// &lt;root&gt;/
///   src/                the entire app, committed -- the agent's work
///     index.html
///     main.tsx
///     App.tsx
///     public/           static assets
///   screenshots/        committed -- the deliverable
///   tsconfig.json       committed, extends .wireframe/tsconfig.base.json
///   .wireframe/         GITIGNORED, regenerated on every run, never user-authored
/// </code>
///
/// Everything the app is made of lives under src/. The root holds only configuration,
/// generated output and docs, so opening the project in an editor shows one folder to
/// work in rather than app files scattered beside config.
///
/// Build output deliberately does NOT live under the project: it goes to a per-project
/// directory under the user's local app data, so there is no stray dist/ to gitignore and
/// `screenshot` can build in isolation without racing a running `serve`.
/// </summary>
public sealed class WireframeProject
{
    public required string Root { get; init; }

    public string SourceDir => Path.Combine(Root, "src");

    /// <summary>Static assets. Inside src/ because they are part of the app, not config.</summary>
    public string PublicDir => Path.Combine(SourceDir, "public");
    public string ScreenshotsDir => Path.Combine(Root, "screenshots");

    /// <summary>
    /// Where the agent writes notes back to whoever maintains the library -- a missing
    /// component, a prop that should exist, something the reference got wrong. Plain HTML
    /// pages, because the agent already knows how to write one and Studio can show it
    /// without a format to agree on first.
    /// </summary>
    public string SuggestionsDir => Path.Combine(Root, "suggestions");
    public string WorkDir => Path.Combine(Root, ".wireframe");
    public string TypesDir => Path.Combine(WorkDir, "types");
    public string StampFile => Path.Combine(WorkDir, ".stamp");
    public string IndexHtml => Path.Combine(SourceDir, "index.html");

    /// <summary>Where index.html used to live. Kept only so existing projects can be
    /// migrated on the next run rather than silently losing their edited markup.</summary>
    public string LegacyIndexHtml => Path.Combine(Root, "index.html");

    /// <summary>Ditto for public/.</summary>
    public string LegacyPublicDir => Path.Combine(Root, "public");
    public string TsConfig => Path.Combine(Root, "tsconfig.json");
    public string EntryPoint => Path.Combine(SourceDir, "main.tsx");

    public string Name => new DirectoryInfo(Root).Name;

    public static WireframeProject At(string path) =>
        new() { Root = Path.GetFullPath(path) };

    public bool Exists => Directory.Exists(SourceDir) && File.Exists(EntryPoint);

    /// <summary>
    /// Build output directory, keyed by a hash of the project path. Separate subdirectories
    /// per purpose so a `screenshot` run can never observe a half-written `serve` bundle.
    /// </summary>
    public string OutDir(string purpose)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Root.ToLowerInvariant())))[..12].ToLowerInvariant();

        var baseDir = Environment.GetEnvironmentVariable("WIREFRAME_CACHE")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "wireframe");

        return Path.Combine(baseDir, "builds", $"{SanitizeName(Name)}-{hash}", purpose);
    }

    private static string SanitizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "wireframe" : s;
    }

    /// <summary>Extensions that count as app source: what the editor lists and the class
    /// linter scans. index.html is included because it lives in src/ and carries markup.</summary>
    private static readonly string[] SourceExtensions = [".tsx", ".ts", ".html", ".css"];

    /// <summary>Files the agent authors. Used by the linter and by Studio's editor.</summary>
    public IEnumerable<string> SourceFiles() =>
        Directory.Exists(SourceDir)
            ? Directory.EnumerateFiles(SourceDir, "*.*", SearchOption.AllDirectories)
                .Where(f => SourceExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            : [];

    public string RelativePath(string absolute) =>
        Path.GetRelativePath(Root, absolute).Replace('\\', '/');
}

using System.Text.Json;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Build;
using Ivy.Tendril.Wireframe.Project;

namespace Ivy.Tendril.Test.Wireframe;

public class WireframeProjectTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wireframe-tests", Guid.NewGuid().ToString("N")[..10]);

    public void Dispose()
    {
        WireframeTempRoot.Remove(_root);
        GC.SuppressFinalize(this);
    }

    private WireframeProject Scaffold()
    {
        var project = WireframeProject.At(_root);
        new ProjectScaffolder(AssetCatalog.Default).Scaffold(project);
        return project;
    }

    [Fact]
    public void Scaffold_produces_a_project_serve_recognises()
    {
        var project = Scaffold();

        Assert.True(project.Exists);
        Assert.True(File.Exists(project.EntryPoint));
        Assert.True(File.Exists(Path.Combine(project.SourceDir, "App.tsx")));
        Assert.True(File.Exists(Path.Combine(project.SourceDir, "wireframe-ready.ts")));
        Assert.True(File.Exists(project.TsConfig));
        Assert.True(Directory.Exists(project.ScreenshotsDir));

        // The whole app lives under src/, so opening the project shows one folder to work in.
        Assert.Equal(Path.Combine(project.SourceDir, "index.html"), project.IndexHtml);
        Assert.True(File.Exists(project.IndexHtml));
        Assert.False(File.Exists(Path.Combine(project.Root, "index.html")));

        // Throwaway mockups: no git scaffolding and no empty-folder placeholders.
        Assert.False(File.Exists(Path.Combine(project.Root, ".gitignore")));
        Assert.Empty(Directory.EnumerateFiles(project.Root, ".gitkeep", SearchOption.AllDirectories));
    }

    [Fact]
    public void Scaffold_wires_up_the_pieces_the_library_requires()
    {
        var project = Scaffold();
        var main = File.ReadAllText(project.EntryPoint);

        // Without SketchProvider the SVG filters never mount; without the tendril class
        // there is no handwriting font or ink colour. Either omission renders the
        // components, but not as a wireframe.
        Assert.Contains("SketchProvider", main);
        Assert.Contains("className=\"tendril\"", main);
        Assert.Contains("signalWireframeReady", main);
    }

    [Fact]
    public void Scaffold_never_clobbers_the_agents_work()
    {
        var project = Scaffold();
        var app = Path.Combine(project.SourceDir, "App.tsx");
        File.WriteAllText(app, "// my wireframe");

        var result = new ProjectScaffolder(AssetCatalog.Default).Scaffold(project);

        Assert.Equal("// my wireframe", File.ReadAllText(app));
        Assert.Contains("src/App.tsx", result.Skipped);
    }

    [Fact]
    public void Workspace_is_regenerated_when_the_payload_changes()
    {
        var project = Scaffold();
        var scaffolder = new ProjectScaffolder(AssetCatalog.Default);

        Assert.False(scaffolder.NeedsRefresh(project));

        // Simulate a tool upgrade: a stamp from a different payload.
        File.WriteAllText(project.StampFile,
            JsonSerializer.Serialize(new { toolVersion = "0.0.0", artifactsHash = "stale" }));

        Assert.True(scaffolder.NeedsRefresh(project));

        scaffolder.MaterializeWorkspace(project);
        Assert.False(scaffolder.NeedsRefresh(project));
    }

    [Fact]
    public void Tsconfig_paths_point_at_types_that_were_actually_vendored()
    {
        var project = Scaffold();
        var baseConfig = File.ReadAllText(Path.Combine(project.WorkDir, "tsconfig.base.json"));

        Assert.Contains("\"react\"", baseConfig);
        Assert.Contains("\"tendril-wireframes\"", baseConfig);
        Assert.True(File.Exists(Path.Combine(project.TypesDir, "tendril-wireframes", "index.d.ts")));

        // types/typeRoots must be empty, or TS hunts for a node_modules/@types that will
        // never exist and reports every import as unresolved.
        Assert.Contains("\"types\": []", baseConfig);
        Assert.Contains("\"typeRoots\": []", baseConfig);
    }

    [Fact]
    public void An_old_layout_project_is_migrated_into_src()
    {
        // Projects scaffolded before the app moved under src/ have index.html and public/
        // at the root. Moving rather than regenerating matters: index.html may be edited.
        var project = Scaffold();
        File.Delete(project.IndexHtml);
        File.WriteAllText(project.LegacyIndexHtml, "<!-- hand edited -->");
        Directory.CreateDirectory(project.LegacyPublicDir);
        File.WriteAllText(Path.Combine(project.LegacyPublicDir, "logo.svg"), "<svg/>");

        new ProjectScaffolder(AssetCatalog.Default).Scaffold(project);

        Assert.Equal("<!-- hand edited -->", File.ReadAllText(project.IndexHtml));
        Assert.False(File.Exists(project.LegacyIndexHtml));
        Assert.True(File.Exists(Path.Combine(project.PublicDir, "logo.svg")));
        Assert.False(Directory.Exists(project.LegacyPublicDir));
    }

    [Fact]
    public void Build_output_lives_outside_the_project_and_is_separated_by_purpose()
    {
        var project = Scaffold();

        // Keeps the project clean, and stops a screenshot build racing a running serve.
        Assert.DoesNotContain(project.Root, project.OutDir("serve"));
        Assert.NotEqual(project.OutDir("serve"), project.OutDir("screenshot"));
    }

    [Fact]
    public void Esbuild_arguments_externalise_every_import_map_specifier()
    {
        // If these two lists disagree, the bundle either inlines a second React (breaking
        // every hook) or references a specifier the browser cannot resolve.
        var project = Scaffold();
        var vendor = VendorManifest.Load(AssetCatalog.Default);
        var args = new EsbuildBundler("esbuild", project, vendor).BuildArguments("out");

        foreach (var specifier in vendor.Specifiers.Keys)
            Assert.Contains($"--external:{specifier}", args);

        Assert.Contains("--format=esm", args);
        Assert.Contains("--jsx=automatic", args);
        Assert.DoesNotContain("--packages=external", args);
    }

    [Fact]
    public void Deep_subpath_specifiers_are_externalised_before_their_parent_package()
    {
        // esbuild matches --external: in order, so "roughjs" ahead of
        // "roughjs/bin/generator" would swallow the deep import.
        var vendor = VendorManifest.Load(AssetCatalog.Default);
        var ordered = vendor.ExternalSpecifiers.ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                Assert.False(
                    ordered[j].StartsWith(ordered[i] + "/", StringComparison.Ordinal),
                    $"'{ordered[j]}' must be externalised before its parent '{ordered[i]}'.");
            }
        }
    }
}

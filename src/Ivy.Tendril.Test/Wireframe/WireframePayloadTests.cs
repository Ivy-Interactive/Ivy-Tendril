using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Manifest;

namespace Ivy.Tendril.Test.Wireframe;

/// <summary>
/// Guards the embedded payload. These are the invariants that, when broken, produce
/// failures in the browser rather than at build time, which is exactly where they are
/// most expensive to diagnose.
/// </summary>
public class WireframePayloadTests
{
    private static readonly AssetCatalog Assets = AssetCatalog.Default;

    [Fact]
    public void Payload_contains_every_area_the_server_serves()
    {
        Assert.True(Assets.Exists("vendor.manifest.json"));
        Assert.True(Assets.Exists("tendril.manifest.json"));
        Assert.True(Assets.Exists("css/tendril.css"));
        Assert.True(Assets.Exists("css/wireframe-utilities.css"));
        Assert.True(Assets.Exists("css/fonts.css"));
        Assert.Contains(Assets.List("fonts"), f => f.EndsWith(".woff2"));
        Assert.NotEmpty(Assets.List("types/react"));
    }

    [Fact]
    public void Payload_carries_neither_esbuild_nor_the_studio_ui()
    {
        // esbuild is provisioned on first use, and Studio is not part of Tendril. Either one
        // appearing here means the pipeline regressed to the standalone tool's layout.
        Assert.Empty(Assets.List("esbuild"));
        Assert.Empty(Assets.List("studio"));
    }

    [Fact]
    public void Every_import_map_specifier_resolves_to_a_real_vendor_file()
    {
        // A specifier that is external to esbuild but missing from the payload links fine
        // and then fails in the browser with an opaque module-resolution error.
        var vendor = VendorManifest.Load(Assets);
        Assert.NotEmpty(vendor.Specifiers);

        foreach (var (specifier, url) in vendor.Specifiers)
        {
            var key = url.Replace("/__wireframe/", "");
            Assert.True(Assets.Exists(key), $"'{specifier}' maps to '{url}', which is not in the payload.");
        }
    }

    [Fact]
    public void React_and_tendril_are_both_present_so_the_page_can_actually_mount()
    {
        var vendor = VendorManifest.Load(Assets);
        Assert.Contains("react", vendor.Specifiers.Keys);
        Assert.Contains("react/jsx-runtime", vendor.Specifiers.Keys);
        Assert.Contains("react-dom/client", vendor.Specifiers.Keys);
        Assert.Contains("tendril-wireframes", vendor.Specifiers.Keys);
    }

    [Fact]
    public void Stylesheets_do_not_reference_Google_Fonts()
    {
        // The shipped tendril.css imports Balsamiq Sans from fonts.googleapis.com. Leaving
        // it in breaks the offline promise and makes screenshots non-deterministic, because
        // the fallback font has different metrics, so different measured boxes and different
        // rough.js geometry.
        foreach (var sheet in new[] { "css/tendril.css", "css/theme.css" })
        {
            var css = Assets.ReadText(sheet);
            Assert.DoesNotContain("fonts.googleapis.com", css);
            Assert.DoesNotContain("fonts.gstatic.com", css);
        }
    }

    [Fact]
    public void Fonts_css_points_at_locally_served_files_that_exist()
    {
        var css = Assets.ReadText("css/fonts.css");
        var matches = System.Text.RegularExpressions.Regex.Matches(css, @"url\(""([^""]+)""\)");
        Assert.NotEmpty(matches);

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var url = m.Groups[1].Value;
            Assert.StartsWith("/__wireframe/fonts/", url);
            Assert.True(Assets.Exists(url.Replace("/__wireframe/", "")), $"{url} is missing from the payload.");
        }
    }

    [Fact]
    public void Utility_sheet_supplies_what_the_library_sheet_lacks()
    {
        // tendril.css carries only the classes its own components use. These are the ones an
        // agent reaches for within minutes of starting, and a missing utility fails silently:
        // the class is in the DOM with no rule behind it.
        var utilities = Assets.ReadText("css/wireframe-utilities.css");

        foreach (var cls in new[]
        {
            "grid-cols-2", "grid-cols-3", "grid-cols-4", "col-span-2",
            "space-y-4", "mt-4", "mb-6", "mx-auto",
            "gap-8", "p-8", "max-w-md", "min-h-screen", "text-4xl", "rounded-lg",
        })
        {
            Assert.Contains($".{cls}", utilities);
        }
    }

    [Fact]
    public void Utility_sheet_emits_into_the_library_cascade_layers_and_adds_no_second_reset()
    {
        // tendril.css declares "@layer properties, theme, base, utilities" first, which fixes
        // that order for the document. Our sheet must land in the same layers; an unlayered
        // one, or one that re-emits preflight, either loses every rule or applies the reset
        // twice.
        var utilities = Assets.ReadText("css/wireframe-utilities.css");
        Assert.Contains("@layer utilities", utilities);
        Assert.Contains("@layer theme", utilities);
        Assert.DoesNotContain("@layer base", utilities);
    }

    [Fact]
    public void Manifest_exposes_the_documented_public_component_count()
    {
        var manifest = ComponentManifest.Load(Assets);
        Assert.Equal(manifest.ComponentCount, manifest.PublicComponents.Count());
        Assert.Contains(manifest.PublicComponents, c => c.Name == "Button");
        Assert.Contains(manifest.PublicComponents, c => c.Name == "SketchProvider");

        // Internal building blocks must not be advertised to an agent.
        Assert.DoesNotContain(manifest.PublicComponents, c => c.Name == "SketchLayer");
        Assert.DoesNotContain(manifest.PublicComponents, c => c.Name == "InputShell");
    }
}

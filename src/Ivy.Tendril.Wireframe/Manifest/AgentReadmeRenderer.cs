using System.Text;
using Ivy.Tendril.Wireframe.Assets;

namespace Ivy.Tendril.Wireframe.Manifest;

/// <summary>
/// Renders the instructions an agent needs to drive this CLI and author wireframes.
///
/// The raw manifest is ~300 KB of JSON, which is useless in a context window. This
/// compresses it to the shape an agent actually reads: the rules that are easy to get
/// wrong, then every public component with its own props on one line each.
/// </summary>
public sealed class AgentReadmeRenderer(ComponentManifest manifest, VendorManifest vendor)
{
    public string Render()
    {
        var sb = new StringBuilder();
        Header(sb);
        Cli(sb);
        Rules(sb);
        Styling(sb);
        SharedProps(sb);
        Enums(sb);
        Components(sb);
        return sb.ToString();
    }

    /// <summary>Full detail for one component, for `--component Button`.</summary>
    public string RenderComponent(ComponentInfo component)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {component.Name}");
        sb.AppendLine();
        sb.AppendLine($"`{component.Category}`" +
            (component.Ivy is not null ? $" · mirrors `{component.Ivy}`" : "") +
            (component.Status is not null ? $" · {component.Status}" : ""));
        sb.AppendLine();

        if (component.Description is not null)
        {
            sb.AppendLine(Flatten(component.Description));
            sb.AppendLine();
        }

        sb.AppendLine("| prop | type | default | notes |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var p in component.Props)
        {
            var type = p.Values is { Count: > 0 } ? string.Join(" \\| ", p.Values) : p.Type;
            var notes = new List<string>();
            if (p.Required) notes.Add("**required**");
            if (p.Kind is not null) notes.Add(p.Kind);
            if (p.Inherited is not null) notes.Add($"from {p.Inherited}");
            if (p.Description is not null) notes.Add(Flatten(p.Description));

            sb.AppendLine($"| `{p.Name}` | `{type}` | {(p.Default is null ? "" : $"`{p.Default}`")} " +
                          $"| {string.Join("; ", notes)} |");
        }

        if (component.Examples.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("```tsx");
            foreach (var e in component.Examples) sb.AppendLine(e);
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private void Header(StringBuilder sb)
    {
        sb.AppendLine("# Building wireframes with `tendril wireframe`");
        sb.AppendLine();
        sb.AppendLine(
            $"`tendril-wireframes@{manifest.Version}` is a React component library that draws itself by " +
            "hand: every border, fill and chart is a rough.js path, so the output reads as a pencil " +
            "sketch rather than a finished design. Use it to mock up screens that nobody should mistake " +
            "for a final UI.");
        sb.AppendLine();
        sb.AppendLine(
            $"The CLI bundles React {vendor.ReactVersion}, the component library and a Tailwind utility " +
            "sheet. **There is no `node_modules`, no `npm install` and no network access required**: " +
            "do not try to add dependencies, and do not write a `package.json`.");
        sb.AppendLine();
    }

    private static void Cli(StringBuilder sb)
    {
        sb.AppendLine("## Commands");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("tendril wireframe setup <path>          # scaffold a project (fast, offline)");
        sb.AppendLine("tendril wireframe serve <path>          # live preview on a free port; --open to launch a browser");
        sb.AppendLine("tendril wireframe screenshot <path>     # -> <path>/screenshots/<width>x<height>.png");
        sb.AppendLine("tendril wireframe agent-readme          # this document; --component <Name> for one component");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("`screenshot` takes `-w/--width` (default 1440) and `--height` (default 900) in CSS " +
                      "pixels, plus `-s/--scale` (default 2, so the PNG is 2880x1800) and `--full` for the " +
                      "whole page height. It builds and serves on its own; you do not need `serve` running.");
        sb.AppendLine();
        sb.AppendLine("**Working loop:** edit `src/App.tsx`, run `tendril wireframe screenshot <path>`, look at the " +
                      "PNG, adjust. `serve` is for a human watching; `screenshot` is how you check your own " +
                      "work.");
        sb.AppendLine();
    }

    private static void Rules(StringBuilder sb)
    {
        sb.AppendLine("## Rules that are easy to get wrong");
        sb.AppendLine();
        sb.AppendLine("1. **Keep `SketchProvider` and the `tendril` class.** `src/main.tsx` wraps the app in " +
                      "`<SketchProvider>` and a `<div className=\"tendril\">`. The provider mounts the SVG " +
                      "filters; the class applies the handwriting font and ink colour. Remove either and the " +
                      "components render, but not as a wireframe.");
        sb.AppendLine();
        sb.AppendLine("2. **Keep the `signalWireframeReady()` call** in `src/main.tsx`. It tells `screenshot` " +
                      "when the sketch has finished drawing. Without it captures fall back to a heuristic and " +
                      "may catch a half-drawn frame.");
        sb.AppendLine();
        sb.AppendLine("3. **Props are named enums, not booleans plus classes.** Write `variant=\"Destructive\"`, " +
                      "`density=\"Small\"`, `borderRadius=\"Full\"`: PascalCase values, exactly as listed below. " +
                      "`variant=\"destructive\"` is not the same thing and will not match.");
        sb.AppendLine();
        sb.AppendLine("4. **Sizes** accept CSS lengths (`\"20rem\"`), fractions (`\"1/2\"`) and bare numbers, " +
                      "which mean quarter-rem steps, so `width={4}` is `1rem`.");
        sb.AppendLine();
        sb.AppendLine("5. **Only these imports resolve.** There is no package manager here:");
        sb.AppendLine();
        sb.AppendLine("   ```tsx");
        sb.AppendLine("   import { ... } from \"tendril-wireframes\";");
        sb.AppendLine("   import { useState } from \"react\";");
        sb.AppendLine("   import { createRoot } from \"react-dom/client\";");
        sb.AppendLine("   import { Rocket } from \"lucide-react\";   // or just icon=\"Rocket\"");
        sb.AppendLine("   ```");
        sb.AppendLine();
        sb.AppendLine("   Importing anything else fails the build with a clear message. There is no charting " +
                      "library to reach for; the chart components below draw themselves.");
        sb.AppendLine();
        sb.AppendLine("6. **Structural layout is plain CSS.** Tendril covers what is worth drawing by hand; " +
                      "flexbox and grid already say the rest with less. Use `TabsLayout`, `SidebarLayout`, " +
                      "`ResizablePanelGroup` and `FloatingPanel` for drawn chrome, and `div` + utilities for " +
                      "everything else.");
        sb.AppendLine();
        sb.AppendLine("7. **A wireframe is throwaway.** It exists to show a person what will be built and to guide " +
                      "whoever builds it, never to ship. Keep the `@tendril-wireframe plan-only` line that " +
                      "`setup` puts at the top of each file, and add it to any file you create. Never copy a " +
                      "wireframe's files, components or imports into product code: Tendril refuses a plan's " +
                      "changes when they turn up there.");
        sb.AppendLine();
        sb.AppendLine("8. **Use relative URLs for your own files.** Write `<img src=\"logo.png\">`, not " +
                      "`/logo.png`. A plan preview serves the wireframe under its own address, where a " +
                      "root-relative URL points somewhere else.");
        sb.AppendLine();
    }

    private static void Styling(StringBuilder sb)
    {
        sb.AppendLine("## Tailwind support");
        sb.AppendLine();
        sb.AppendLine("A large precompiled Tailwind v4 sheet ships inside the CLI. The common surface is " +
                      "present: the full spacing scale for `p/m/gap/space/w/h/inset`, `grid-cols-1..12` and " +
                      "`col-span-*`, flex and alignment, `text-xs..9xl`, font weights, radii, borders, " +
                      "shadows, `overflow-*`, positioning, `z-*`, `opacity-*`, and the `sm: md: lg: xl:` and " +
                      "`hover: focus: active: disabled:` variants.");
        sb.AppendLine();
        sb.AppendLine("**Arbitrary values like `w-[347px]` are NOT generated**: the sheet is compiled ahead " +
                      "of time, so a class it does not contain silently does nothing. For one-off values use " +
                      "an inline style or the component's own size prop:");
        sb.AppendLine();
        sb.AppendLine("```tsx");
        sb.AppendLine("<div style={{ width: 347 }} />        // instead of className=\"w-[347px]\"");
        sb.AppendLine("<Card width=\"20rem\" />                // components take sizes directly");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("If a project genuinely needs arbitrary values, it can be set up with " +
                      "`tendril wireframe setup <path> --tailwind jit`, which downloads the real Tailwind CLI " +
                      "(~107 MB, once) and generates whatever the source asks for. Assume that is NOT " +
                      "on unless you see a `wireframe.json` saying so.");
        sb.AppendLine();
        sb.AppendLine("Theme colours are available as `bg-`/`text-`/`border-` utilities: `ink`, `ink-muted`, " +
                      "`ink-faint`, `paper`, `paper-raised`, `paper-sunken`, `highlight`, `accent`, `success`, " +
                      "`warning`, `destructive`, `info`. Prefer these over raw Tailwind palette colours so the " +
                      "wireframe stays monochrome.");
        sb.AppendLine();
        // The vocabulary for colour *props* used to be nowhere in this document, while the
        // utility vocabulary was right above, so a prop called `background` looked as though
        // it took `bg-paper-sunken`, and an unresolvable value painted solid black.
        sb.AppendLine("### Colour values");
        sb.AppendLine();
        sb.AppendLine("Props that take a colour (`color`, `background`, `foreground`, `stroke`, `fill`, " +
                      "`borderColor`) accept any of three things:");
        sb.AppendLine();
        sb.AppendLine("```tsx");
        sb.AppendLine("<Box background=\"paper-sunken\" />   // a theme token, as listed above");
        sb.AppendLine("<Badge color=\"Destructive\" />        // an Ivy palette name, PascalCase");
        sb.AppendLine("<Box background=\"#efe9dd\" />         // any CSS colour");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Note the `bg-` prefix belongs to the *class* form only: `background=\"bg-paper-sunken\"` " +
                      "is not a colour. An unrecognised value falls back to the default and warns in the " +
                      "browser console rather than rendering.");
        sb.AppendLine();
    }

    private void SharedProps(StringBuilder sb)
    {
        sb.AppendLine("## Shared prop sets");
        sb.AppendLine();
        sb.AppendLine("Documented once here and omitted from the per-component listings. " +
                      "Components that take them are marked, e.g. `+BaseInputProps`.");
        sb.AppendLine();

        foreach (var source in PropSources.Shared)
        {
            var props = manifest.Components
                .SelectMany(c => c.Props)
                .Where(p => p.Inherited == source)
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            if (props.Count == 0) continue;

            sb.AppendLine($"**{source}**" +
                (source == "WidgetBaseProps" ? ": every component accepts these" : ""));
            foreach (var p in props)
                sb.AppendLine($"  - `{p.Signature()}`" +
                              (p.Description is not null ? $": {Flatten(p.Description)}" : ""));
            sb.AppendLine();
        }

        sb.AppendLine("Every component also takes `className`, `style` and `data-testid`, and most " +
                      "forward the standard React DOM attributes (`onClick`, `aria-*`, ...), which are " +
                      "not listed.");
        sb.AppendLine();
    }

    /// <summary>
    /// Every named type the prop signatures use.
    ///
    /// Enums are one line each and there are dozens of them, so they stay a flat list.
    /// Objects and aliases get their own block: a prop typed `Sizing` or `Option` is
    /// meaningless without one, and leaving them out is what let an agent read
    /// `width: Sizing`, assume pixels, and render a box four times too big.
    /// </summary>
    private void Enums(StringBuilder sb)
    {
        sb.AppendLine("## Types");
        sb.AppendLine();
        sb.AppendLine("Referenced by the prop signatures below.");
        sb.AppendLine();

        var ordered = manifest.Types.OrderBy(t => t.Key, StringComparer.Ordinal).ToList();

        var enums = ordered.Where(t => t.Value.Kind == "enum" && t.Value.Values is not null).ToList();
        if (enums.Count > 0)
        {
            sb.AppendLine("### Enums");
            sb.AppendLine();
            foreach (var (name, info) in enums)
                sb.AppendLine($"- `{name}` = {string.Join(" | ", info.Values!)}");
            sb.AppendLine();
        }

        var shapes = ordered.Where(t => t.Value.Kind is not "enum").ToList();
        if (shapes.Count == 0) return;

        sb.AppendLine("### Shapes and aliases");
        sb.AppendLine();

        foreach (var (name, info) in shapes)
        {
            switch (info.Kind)
            {
                case "alias":
                {
                    // A union written across lines starts with a leading "|" in the source,
                    // which is idiomatic TypeScript and noise on one line.
                    var expansion = info.Type?.TrimStart('|').TrimStart() ?? "";
                    var members = SplitUnion(expansion);

                    // A union of object shapes on one line is technically documented and
                    // practically unreadable: `SketchShape` came out as a single 600-character
                    // run, and an agent gave up on `RoughShape` rather than parse it. One
                    // member per line is the same information, legibly.
                    if (members.Count > 2 && expansion.Length > 90)
                    {
                        sb.AppendLine($"**`{name}`** is one of:");
                        sb.AppendLine();
                        foreach (var member in members) sb.AppendLine($"  - `{member}`");
                    }
                    else
                    {
                        sb.AppendLine($"**`{name}`** = {expansion}");
                    }
                    break;
                }
                case "map":
                    sb.AppendLine($"**`{name}`** = {{ [key: {info.KeyType}]: {info.ValueType} }}");
                    break;
                default:
                    sb.AppendLine($"**`{name}`**");
                    break;
            }

            if (info.Description is not null)
            {
                sb.AppendLine();
                sb.AppendLine(Flatten(info.Description));
            }

            if (info.Properties is { Count: > 0 })
            {
                sb.AppendLine();
                foreach (var property in info.Properties)
                {
                    var optional = property.Required ? "" : "?";
                    var note = property.Description is not null
                        ? $": {Flatten(property.Description)}"
                        : "";
                    sb.AppendLine($"  - `{property.Name}{optional}: {property.Type}`{note}");
                }
            }

            sb.AppendLine();
        }
    }

    /// <summary>
    /// Splits a union into its members, ignoring the `|` that appear *inside* a member.
    ///
    /// `{ a: string | number } | { b: number }` is two members, not three. Splitting on the
    /// bare separator would cut the first shape in half and produce something that looks
    /// like valid syntax but is not, which is worse than not splitting at all.
    /// </summary>
    private static List<string> SplitUnion(string expansion)
    {
        var members = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < expansion.Length; i++)
        {
            var c = expansion[i];
            if (c is '{' or '(' or '[' or '<') depth++;
            else if (c is '}' or ')' or ']' or '>') depth--;
            else if (c == '|' && depth == 0)
            {
                members.Add(expansion[start..i].Trim());
                start = i + 1;
            }
        }

        members.Add(expansion[start..].Trim());
        return members.Where(m => m.Length > 0).ToList();
    }

    private void Components(StringBuilder sb)
    {
        sb.AppendLine("## Components");
        sb.AppendLine();

        var publicComponents = manifest.PublicComponents.ToList();
        sb.AppendLine($"{publicComponents.Count} components. `prop?` means optional; `= X` is the default.");
        sb.AppendLine();

        foreach (var group in publicComponents
                     .GroupBy(c => c.Category)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();

            foreach (var component in group.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                sb.AppendLine($"**{component.Name}**" +
                    (component.Description is not null ? $": {Flatten(component.Description)}" : ""));

                var props = component.OwnProps.ToList();
                var bases = component.SharedBases.ToList();

                var line = props.Count == 0 && bases.Count == 0
                    ? "  - *(no props of its own)*"
                    : "  - " + string.Join(" · ", props.Select(p => p.Signature())
                        .Concat(bases.Select(b => "+" + b)));
                sb.AppendLine(line);

                foreach (var example in component.Examples.Take(2))
                    sb.AppendLine($"  - `{example}`");

                sb.AppendLine();
            }
        }
    }

    /// <summary>
    /// Collapses the manifest's embedded newlines so a description stays on one line. Em dashes
    /// from the library's docs become plain hyphens, since Tendril text never carries them.
    /// </summary>
    private static string Flatten(string text) =>
        string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()))
            .Replace("|", "\\|")
            .Replace("\u2014", "-");
}

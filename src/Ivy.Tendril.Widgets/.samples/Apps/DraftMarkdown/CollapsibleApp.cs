using Ivy;
using Ivy.Tendril.Widgets;
using DraftMarkdownWidget = Ivy.Tendril.Widgets.PlanMarkdown;

namespace WidgetSamples.Apps.DraftMarkdown;

[App(title: "Collapsible", icon: Icons.ListCollapse, group: ["DraftMarkdown"])]
class CollapsibleApp : ViewBase
{
    public override object Build()
    {
        var markdown = """
            # Collapsible Sections

            GitHub-style `<details>` / `<summary>` blocks render as real disclosure
            widgets. Tendril's own promptware emits them (the plan `## Questions`
            section), so plan markdown routinely contains raw HTML.

            ## Basic

            <details>
            <summary>What database does the plan target?</summary>

            PostgreSQL 16. The migration scripts live in `db/migrations` and are
            applied by the `MigrateDatabase` job on startup.

            </details>

            <details>
            <summary>Do we need a feature flag?</summary>

            No. The change is additive and the old code path is removed in the same
            commit, so there is nothing to toggle between.

            </details>

            ## Open by Default

            <details open>
            <summary>Still relevant?</summary>

            Yes. The `open` attribute survives sanitisation, so a section can start
            expanded.

            </details>

            ## Rich Bodies

            A section body is ordinary markdown — lists, tables, code and diagrams all
            work inside one.

            <details>
            <summary>Migration steps</summary>

            1. Snapshot the current schema.
            2. Apply the new migration.
            3. Verify row counts match.

            | Step | Owner | Duration |
            | --- | --- | --- |
            | Snapshot | ops | 5 min |
            | Migrate | api | 2 min |
            | Verify | api | 10 min |

            ```bash
            dotnet run --project src/Migrator -- --verify
            ```

            > [!WARNING]
            > The snapshot must complete before the migration starts.

            </details>

            ## Nesting

            <details>
            <summary>Rejected alternatives</summary>

            Three approaches were considered and dropped.

            <details>
            <summary>Dual-write to both stores</summary>

            Rejected: no way to make the two writes atomic, so a crash between them
            leaves the stores permanently diverged.

            </details>

            <details>
            <summary>Change data capture</summary>

            Rejected: adds a Debezium deployment for a one-off migration.

            </details>

            </details>

            ## Sanitisation

            The markdown is model-written, so raw HTML is parsed and then pruned
            against an allow-list. Everything below is stripped and none of it reaches
            the DOM — the section renders, its attack payloads do not.

            <details onclick="alert('handler')" style="background: red">
            <summary>Event handlers and inline styles are removed</summary>

            <script>alert("script")</script>
            <iframe src="https://example.test"></iframe>
            <style>body { display: none }</style>

            Scripts, iframes and style blocks are dropped entirely, while ordinary
            inline markup such as <b>bold</b>, <kbd>Ctrl</kbd> and
            <a href="https://ivy.app">a link</a> is kept.

            </details>

            <details>
            <summary>Unsafe link protocols are neutralised</summary>

            This anchor keeps its text but loses its href:
            <a href="javascript:alert(1)">javascript: link</a>

            </details>
            """;

        return new DraftMarkdownWidget(markdown).Article();
    }
}

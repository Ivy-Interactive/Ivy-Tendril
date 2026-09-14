namespace Ivy.Tendril.Wireframe.Project;

/// <summary>
/// The files `wireframe setup` writes into a new project.
///
/// The whole app lives under src/ -- index.html, the entry point, the components and any
/// static assets. The project root holds only configuration (tsconfig.json, .gitignore),
/// generated output (screenshots/, .wireframe/) and docs.
///
/// These are the first thing an agent reads, so they double as documentation: the starter
/// App.tsx demonstrates the conventions (named-enum props, sizing values, the
/// SketchProvider/.tendril wrappers) that the component library expects.
/// </summary>
public static class ScaffoldTemplates
{
    /// <summary>
    /// Stamped at the top of every file `setup` writes. Wireframes are throwaway plan material, and
    /// Tendril's leak guard refuses a plan's changes when this marker turns up in a product repo: a
    /// copied file carries it along.
    /// </summary>
    public const string PlanOnlyMarker = "@tendril-wireframe plan-only";

    public const string IndexHtml =
        """
        <!doctype html>
        <!-- @tendril-wireframe plan-only: a throwaway mockup. Never copy it into product code. -->
        <html lang="en">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>{{TITLE}}</title>
            <!--
              Stylesheets are injected by the dev server, in this order:
                1. tendril.css             the library sheet (carries Tailwind's preflight)
                2. wireframe-utilities.css the Tailwind utility superset
                3. fonts.css               self-hosted Balsamiq Sans
              Order matters: the utility sheet must come after the library sheet so its
              rules win within the shared @layer utilities.
            -->
          </head>
          <body>
            <div id="root"></div>
          </body>
        </html>

        """;

    public const string MainTsx =
        """
        // @tendril-wireframe plan-only: a throwaway mockup. Never copy it into product code.
        import { createRoot } from "react-dom/client";
        import { SketchProvider } from "tendril-wireframes";
        import { signalWireframeReady } from "./wireframe-ready";
        import App from "./App";

        // SketchProvider mounts the shared SVG filters and sets the pencil (roughness,
        // bowing, stroke width) for everything beneath it. The `tendril` class applies the
        // handwriting font and ink colour. Both are required -- without them the components
        // render, but not as a hand-drawn wireframe.
        createRoot(document.getElementById("root")!).render(
          <SketchProvider>
            <div className="tendril">
              <App />
            </div>
          </SketchProvider>
        );

        // Tells `wireframe screenshot` when the sketch has finished drawing. Leave this in.
        signalWireframeReady();

        """;

    /// <summary>
    /// Deliberately blank. A starter wireframe here would be the first thing an agent has
    /// to delete, and the first thing it copies the style of by accident -- so `setup`
    /// leaves an empty page and the conventions live in `wireframe agent-readme`.
    /// </summary>
    public const string AppTsx =
        """
        // @tendril-wireframe plan-only: a throwaway mockup. Never copy it into product code.
        // Build the wireframe here. Components come from "tendril-wireframes"; layout is
        // plain flexbox and CSS grid. Run `tendril wireframe agent-readme` for the full reference.
        export default function App() {
          return <div />;
        }

        """;

    /// <summary>
    /// The screenshot readiness contract. The ordering here is load-bearing -- see the
    /// comments inline; getting it wrong produces screenshots of half-drawn wireframes.
    /// </summary>
    public const string WireframeReadyTs =
        """
        // @tendril-wireframe plan-only: a throwaway mockup. Never copy it into product code.
        // Signals when the wireframe has finished drawing, so `wireframe screenshot` knows
        // when to capture. Import and call this once from main.tsx.
        //
        // Why this is more involved than "wait for load": every border and fill in Tendril
        // is an SVG path that rough.js generates AFTER a ResizeObserver measures the
        // element's box. A frame with no measurement yet renders nothing at all, so the
        // first painted frame of any wireframe is empty.

        type ReadyState = {
          version: 1;
          ready: boolean;
          reason: string | null;
          deterministic: boolean;
          whenReady: () => Promise<void>;
        };

        declare global {
          interface Window {
            __wireframe?: ReadyState;
          }
        }

        const QUIET_MS = 120;      // no ResizeObserver callback for this long => settled
        const HARD_CAP_MS = 10_000; // never hang, even if something animates forever

        export function signalWireframeReady(root: HTMLElement = document.body): void {
          let resolve!: () => void;
          const promise = new Promise<void>((r) => (resolve = r));

          const state: ReadyState = {
            version: 1,
            ready: false,
            reason: "starting",
            deterministic: true,
            whenReady: () => promise,
          };
          window.__wireframe = state;

          let quiet: ReturnType<typeof setTimeout> | undefined;
          const observer = new ResizeObserver(() => bump("layout still settling"));

          function observeAll() {
            observer.disconnect();
            root.querySelectorAll<HTMLElement>("*").forEach((el) => observer.observe(el));
          }

          function bump(reason: string) {
            state.reason = reason;
            clearTimeout(quiet);
            quiet = setTimeout(finish, QUIET_MS);
          }

          function finish() {
            observer.disconnect();
            // A focused input blinks its caret, which is a one-pixel diff between two
            // otherwise identical screenshots.
            if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
            state.ready = true;
            state.reason = null;
            document.documentElement.setAttribute("data-wireframe-ready", "true");
            resolve();
          }

          void (async () => {
            // 1. Fonts FIRST, and awaited alone. Swapping in Balsamiq Sans changes text
            //    metrics, which resizes every frame, which regenerates every rough.js path.
            //    Capturing between those two states gives strokes sized for the fallback
            //    font wrapped around correctly-sized text.
            state.reason = "waiting for fonts";
            await document.fonts.ready;

            // 2. Images change measured boxes too.
            state.reason = "waiting for images";
            await Promise.all(
              [...document.images]
                .filter((img) => !img.complete)
                .map((img) => new Promise((r) => {
                  img.onload = r;
                  img.onerror = r;
                }))
            );

            // 3. Let React commit the post-measurement render, then let it paint.
            await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));

            // 4. Only now start the quiet clock. The rAFs above guarantee one paint has
            //    happened, but nested frames settle in a cascade -- a Table inside a Card
            //    inside a SidebarLayout can take three rounds -- so the ResizeObserver
            //    quiet period is the signal that actually matters.
            observeAll();
            bump("waiting for quiet period");
          })();

          setTimeout(() => {
            if (!state.ready) {
              state.reason = "timed out";
              finish();
            }
          }, HARD_CAP_MS);
        }

        """;

    public const string TsConfig =
        """
        {
          // Editor support only -- nothing runs tsc. The real type definitions live in
          // .wireframe/types/, which `wireframe setup` regenerates; do not edit them.
          "extends": "./.wireframe/tsconfig.base.json",
          "include": ["src"]
        }

        """;

    /// <summary>
    /// Written into .wireframe/. `paths` is what lets an editor resolve "react" and
    /// "tendril-wireframes" with no node_modules on disk -- VS Code ships its own tsserver,
    /// so this gives IntelliSense without node installed.
    /// </summary>
    public const string TsConfigBase =
        """
        {
          "//": "GENERATED by `wireframe setup` -- do not edit. Regenerated on every run.",
          "compilerOptions": {
            "target": "ES2022",
            "module": "ESNext",
            "moduleResolution": "Bundler",
            "jsx": "react-jsx",
            "jsxImportSource": "react",
            "strict": true,
            "noEmit": true,
            "allowSyntheticDefaultImports": true,
            "esModuleInterop": true,
            "resolveJsonModule": true,
            "isolatedModules": true,
            "skipLibCheck": true,
            "forceConsistentCasingInFileNames": true,
            "lib": ["ES2022", "DOM", "DOM.Iterable"],
            "//types": "Empty types/typeRoots stops TS hunting for a node_modules/@types that will never exist.",
            "types": [],
            "typeRoots": [],
            "baseUrl": "..",
            "paths": {
        {{PATHS}}
            }
          }
        }

        """;
}

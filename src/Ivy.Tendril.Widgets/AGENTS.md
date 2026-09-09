# Ivy.Tendril.Widgets

External widget library for the Ivy framework, bundled as React IIFE modules served from the DLL as embedded resources.

## Project Structure

```
DraftMarkdown.cs      Widget record with [Prop] and [Event] attributes
AgentViewer.cs
TendrilProcessViewer.cs
TendrilUi.cs          The shared primitives as external widgets (see "Shared primitives")
frontend/             React/Vite bundle (npm run build → dist/)
  src/ui/             Tooltip, Kbd, Badge, IconButton, StatusLine + ui.css

.samples/             Standalone Ivy app hosting widgets for development and testing
  Apps/
    DraftMarkdown/    AnnotationsApp, CollapsibleApp, ComparisonApp, MathApp, StickyContentApp
    AgentViewer/      ErrorApp, LiveStreamApp, PreBufferedApp, TableOutputApp
    TendrilProcessViewer/  DemoApp
    ChatWidget/       DemoApp (mocked conversation: attachments, tool calls, job event, questions, streaming toggle)
    TendrilQuestions/ DemoApp (every question case of the plan schema)
    WebViewer/        DemoApp (inspector), SideBySideApp (two viewers on one page)
    Ui/               GalleryApp (every shared primitive and its variants)

.tests/               Playwright E2E tests
  widgets/            Test specs grouped by widget
    draft-markdown/   annotations.spec.ts, collapsible.spec.ts, rendering.spec.ts,
                      sticky-content.spec.ts
  fixtures/           Extended Playwright test fixture (console capture, step screenshots)
  utils/              Server management, navigation helpers, screenshot utilities
  global-setup.ts     Builds .samples, spawns dotnet server on a free port
  global-teardown.ts  Kills the server (Windows taskkill /F /T by PID)
```

## .samples

The samples project (`Ivy.Tendril.Widgets.Samples.csproj`) is a full Ivy application that hosts all widgets in demo apps. It is both a development playground and the test target for E2E tests.

App URL routing: namespace path → kebab-case URL. `WidgetSamples.Apps.DraftMarkdown.AnnotationsApp` → `/draft-markdown/annotations`.

Run manually: `cd .samples && dotnet run -- --port 5100`

## .tests

Playwright-based E2E test suite. Tests exercise the full stack: C# widget → SignalR → React component → DOM.

### Running tests

```bash
cd .tests
npm install                     # first time
npx playwright install chromium # first time
npx playwright test             # run all
npx playwright test --headed    # watch in browser
```

### How it works

1. `global-setup.ts` builds the samples project, finds a free port, spawns `dotnet run`, waits for HTTPS health check.
2. Tests navigate to sample apps, interact with widgets, and assert DOM state.
3. `global-teardown.ts` kills the server process tree.
4. Artifacts (screenshots, logs, backend.log) go to `.tests/artifacts/` (gitignored).

### Key conventions

- Single worker, sequential execution (one shared server instance).
- Server round-trips (SignalR events) use 15s timeouts in `expect()` calls.
- `waitForDraftMarkdown(page)` waits for `.pmv-shell .pmv-markdown` with rendered children.
- `stepScreenshot("description")` captures numbered screenshots for debugging.
- Console errors and page errors are captured per-test to `artifacts/logs/console/`.

## Frontend Build

```bash
cd frontend
npm install
npm run build    # tsc + vite → dist/ivy-tendril-widgets.{js,css}
npm test         # vitest unit tests
```

The bundle is built by MSBuild (via the WidgetsBuildFrontend target) and embedded from `dist/`, which is gitignored.

## WebViewer proxy

The `WebViewer` widget renders only the iframe. The endpoints it depends on (`/__proxy`, `/__view`,
`/__capture`, `/__captures`, `/__lib`, `/sw.js`) are part of the library: `WebViewerProxy.cs` plus
the assets in `proxy-assets/` (injected page agent, service worker, snapDOM), embedded into the DLL.
Host them on the consuming app's own origin:

```csharp
server.ReservePaths(WebViewerProxy.ReservedPaths);
server.UseWebApplication(app => app.MapWebViewerProxy());
```

The service worker registers at scope `/__view/`, so it controls the proxied iframe and never
sees the host app's own traffic; the host page is therefore uncontrolled, and the widget talks
to the worker through the registration rather than `navigator.serviceWorker.controller`.
Worker state that must outlive an idle teardown is kept in the Cache API.

`WebViewerRewriter.cs` maps every URL in proxied HTML and CSS into view-space
(`/__view/<absolute-url>`). HTML goes through AngleSharp, never pattern matching — entity
decoding, script bodies and comment boundaries are the parser's job. Tests live in
`src/Ivy.Tendril.Test/Widgets/WebViewerRewriterTests.cs`.

### Several viewers on one page

Mounted WebViewers share an origin and therefore a single service worker, which has no other
way to tell whose request it is holding. Each viewer's frame carries a token in its own URL —
`/__view/@v3.mobile/<absolute-url>` — naming the viewer and the device it emulates. Only
DOCUMENT urls carry it; rewritten subresources stay bare and the worker resolves them through
the client that asked (`viewContext` in `sw.js`, memoised per client id). `ViewToken` in
`WebViewerRewriter.cs` is the grammar, and `sw.js` and `agent.js` each parse the same thing.

Three consequences worth keeping:

- device emulation is a property of the frame's URL, not of the worker. Setting it on the
  worker made it global (last viewer to mount wins) and had to be re-sent after every idle
  teardown;
- the worker tags each HAR broadcast with the viewer it belongs to, because all viewers share
  one parent window. An untagged entry is only claimed when a single viewer is mounted;
- the parent ignores any `postMessage` whose `source` is not its own frame — otherwise every
  viewer reports its neighbours' clicks and console output as its own.

`.samples/Apps/WebViewer/SideBySideApp.cs` mounts two of them and is where to check this.

### Comment pins

A submitted comment leaves a numbered yellow pin on its element, clickable to edit or delete.
Placement lives in `agent.js` (only the page can resolve an xpath after a re-render, and pins
are positioned in document coordinates, repositioned on scroll/resize/ResizeObserver); the
LIST lives in the widget, which replaces the whole set through `markers-set` on every change
and on every load — the agent is re-injected per document and remembers nothing.

Ivy sees `CommentEvent` / `CommentUpdatedEvent` / `CommentDeletedEvent`. `Id` is the identity;
`Number` is only a 1-based position, so a delete renumbers the survivors with no event of its
own — keep the list in arrival order and the numbers follow.

### Fetching on a caller's behalf

`/__proxy` and `/__resolve` fetch URLs the caller names, gated by
`WebViewerProxyOptions.IsUrlAllowed` (null by default: an open relay, which is what makes it
useful against localhost). Redirects are followed by `WebViewerHttp`, one hop at a time,
through the same gate — the shared `HttpClient` follows none of its own, since a redirect is a
URL the caller did not name and the allow-list would never see it.

No cookies travel in either direction, and `Set-Cookie` is not relayed: every proxied site is
served from the Ivy app's one origin, so a single cookie jar would be shared by all of them.
Sites that need a session cannot be reviewed signed in.

## Markdown raw HTML

`frontend/src/math.ts` builds the remark/rehype plugin lists for every markdown surface
(DraftMarkdown, AgentViewer, ChatWidget, PlanDiffView). GFM is always on; the raw-HTML pair
and the math pair are added only when the content needs them.

Raw HTML matters because Tendril promptware tells agents to emit GitHub-style
`<details>`/`<summary>` blocks (`Promptwares/UpdatePlan/Program.md` builds the plan
`## Questions` section out of them). The content is model-written, so `rehype-raw` parses it
and `rehype-sanitize` immediately prunes it against the allow-list in
`frontend/src/rawHtml.ts`. Two ordering rules hold that pipeline together:

- sanitising runs **before** `rehype-katex`, whose output is a large tree of classed spans,
  inline styles and MathML that the allow-list would strip;
- URL safety is left to react-markdown's `urlTransform`, which runs after sanitising and is
  already the gate for `DangerouslyAllowLocalFiles`.

Styling lives in `frontend/src/DraftMarkdown/draft-markdown.css` (chevron, hover, body inset),
mirroring the framework's `typography.details` / `typography.summary`.

## Shared primitives

`frontend/src/ui` holds the controls every widget in the bundle reuses, so a tooltip, a
shortcut hint, a badge or an icon button looks and behaves the same wherever it appears.
Reach for these before writing a new one; a widget's own CSS should carry only what genuinely
differs (a collapse transition, an on-CTA recolor), never a second copy of the chrome.

| Component | What it replaces |
|---|---|
| `Tooltip` | native `title` on controls, and per-widget tooltip styling. `ShellTooltip` is this tooltip with the shell's default side |
| `Kbd` | `variant="boxed"` is a key cap (tooltips, toolbars); `variant="bare"` is the letters alone, inside a button's own chrome |
| `Badge` / `CountBadge` / `StatusDot` | label chips, notification counts and status dots |
| `IconButton` | square icon controls; it carries the tooltip, so callers pass `label`, not `title` |
| `StatusLine` | the agent status line: spinner, live elapsed time, token count, message |

Two things worth knowing:

- **Tooltips on controls that can be disabled.** A disabled button emits no pointer events, so
  its tooltip — which is exactly the one that says *why* it is disabled — never opens. Pass
  `wrapTrigger` (`IconButton` does it for you whenever the caller passes `disabled`) to anchor
  on a focusable wrapper instead. It describes the control, not its current state: flipping it
  would remount the trigger and drop focus mid-interaction.
- **The status line's token count.** Only a `result` event carries a reported usage, so a run in
  flight can be counted no other way than from its own stream. `AgentViewer/stream-metrics.ts`
  estimates it at ~4 characters per token and flags it, and the status line renders a flagged
  figure with a leading `~`. Once a result arrives the reported figure wins.

Each primitive is also an external widget (`TendrilUi.cs`), so Ivy apps can compose the same
controls. C# enum props arrive as PascalCase member names; `frontend/src/ui/widgets.tsx` maps
them down to the lowercase values the primitives take.

## Widget ↔ Framework Contract

- Widgets register on `window.IvyTendrilWidgets` (matching `GlobalName` in `[ExternalWidget]`).
- Props are passed directly as React component props (camelCase on JS side, PascalCase C# `[Prop]`).
- Events fire via `eventHandler(eventName, widgetId, [args])` — the prop name is `eventHandler`.
- Named slots arrive via `slots.SlotName` (array of React nodes). The slot name preserves PascalCase from the C# `[Slot("Name")]` attribute.
- Non-slot children arrive as React children and also as `slots.default`.

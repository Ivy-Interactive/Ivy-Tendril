# Ivy.Tendril.Widgets

External widget library for the Ivy framework, bundled as React IIFE modules served from the DLL as embedded resources.

## Project Structure

```
DraftMarkdown.cs      Widget record with [Prop] and [Event] attributes
AgentViewer.cs
TendrilProcessViewer.cs
frontend/             React/Vite bundle (npm run build → dist/)

.samples/             Standalone Ivy app hosting widgets for development and testing
  Apps/
    DraftMarkdown/    AnnotationsApp, ComparisonApp, StickyContentApp
    AgentViewer/      ErrorApp, LiveStreamApp, PreBufferedApp, TableOutputApp
    TendrilProcessViewer/  DemoApp

.tests/               Playwright E2E tests
  widgets/            Test specs grouped by widget
    draft-markdown/   annotations.spec.ts, rendering.spec.ts
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

The built bundle is committed to the repo (it's served as an embedded resource from the DLL).

## Widget ↔ Framework Contract

- Widgets register on `window.IvyTendrilWidgets` (matching `GlobalName` in `[ExternalWidget]`).
- Props are passed directly as React component props (camelCase on JS side, PascalCase C# `[Prop]`).
- Events fire via `eventHandler(eventName, widgetId, [args])` — the prop name is `eventHandler`.
- Named slots arrive via `slots.SlotName` (array of React nodes). The slot name preserves PascalCase from the C# `[Slot("Name")]` attribute.
- Non-slot children arrive as React children and also as `slots.default`.

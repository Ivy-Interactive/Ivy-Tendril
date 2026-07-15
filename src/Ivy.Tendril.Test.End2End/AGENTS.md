# Ivy.Tendril.Test.End2End

Playwright-based E2E tests that drive Tendril as a black-box subprocess and verify the full lifecycle: onboarding, plan creation, agent execution, PR creation.

## Quick start

```bash
# IMPORTANT: Pre-build both projects first to avoid MSBuild lock conflicts.
# The test fixture uses `dotnet run` which triggers a build — if the test runner
# is also Creating, MSBuild child nodes collide and restore gets cancelled.
dotnet build src/Ivy.Tendril/Ivy.Tendril.csproj
dotnet build src/Ivy.Tendril.Test.End2End/

# Run with --no-build to avoid the conflict
dotnet test src/Ivy.Tendril.Test.End2End/ --no-build --verbosity normal
```

Individual test classes can be run with `--filter`:

```bash
dotnet test --filter "FullyQualifiedName~OnboardingTests"
dotnet test --filter "FullyQualifiedName~VerificationTests"
dotnet test --filter "FullyQualifiedName~PlanLifecycleTests"
dotnet test --filter "FullyQualifiedName~AgentExecutionTests"
dotnet test --filter "FullyQualifiedName~CleanupTests"
```

To test a specific coding agent:

```bash
E2E__Agent=codex dotnet test --verbosity normal
E2E__Agent=gemini dotnet test --verbosity normal
```

## Architecture

### Fixture lifecycle

```
E2ETestFixture.InitializeAsync()
  |
  ├── PlaywrightFixture  (launches Chromium)     } in parallel
  ├── TestRepositoryFixture (forks Ivy-Templates) }
  |
  └── TendrilProcessFixture (dotnet run --web --verbose --find-available-port)
        - Uses isolated TENDRIL_HOME in %TEMP%
        - Captures all stdout/stderr lines for assertions
        - Polls stdout for server URL via regex
```

All tests share one `E2ETestFixture` instance via `[Collection("E2E")]`. The fixture forks `Ivy-Interactive/Ivy-Templates` (a dotnet project) to a temporary GitHub fork, clones it locally, and starts Tendril pointing at that clone.

### Test repo

The tests use `Ivy-Interactive/Ivy-Templates` — a real dotnet project with `Program.cs`, `GlobalUsings.cs`, `.csproj`, and solution file. This ensures plan descriptions target actual code (e.g., "Uppercase all string literals in Program.cs").

### Key directories at runtime

```
%TEMP%/tendril-e2e-{runId}/          ← TENDRIL_HOME
  config.yaml
  tendril.db
  Plans/
    .counter
    00001-PlanFolderName/
      plan.yaml
      revisions/
  Promptwares/
    CreatePlan/ ExecutePlan/ CreatePr/    ← program folders only, no Logs/
  Jobs/
    .counter                              ← Job ID counter
    {jobId}-CreatePlan.md                 ← Job Log (+ .prompt.md, .raw.jsonl, .eventwire.jsonl)
    {jobId}-{planId}-ExecutePlan.md
  Logs/
    worktrees.log

%TEMP%/tendril-pw-{runId}-artifacts/  ← PromptwareTestFixture.TestArtifactsDir (NOT under TENDRIL_HOME)
  execute-plan-claude.cli.jsonl       ← `--cli-log`: the tendril CLI calls the agent made
```

## What the tests cover

| Test class | Tests | What it verifies |
|---|---|---|
| OnboardingTests | 1 | Full onboarding wizard: welcome → software check → agent selection → home setup → project setup → complete |
| VerificationTests | 4 | Directory structure, config.yaml, tendril.db, dashboard loads |
| PlanLifecycleTests | 1 | Plan creation via UI submits job (verifies job appears in Jobs grid) |
| AgentExecutionTests | 1 | Full lifecycle: CreatePlan → Execute → CreatePR (plan reaches Review, PR URL in plan.yaml) |
| CleanupTests | 2 | Fixture teardown, GitHub fork deletion |

## Common gotchas

- **MSBuild lock conflicts**: The test fixture launches Tendril via `dotnet run`, which triggers a build. If the test runner is also Creating, MSBuild child nodes collide (`MSB4166: Child node exited prematurely`). Always pre-build both projects and run tests with `--no-build`.
- **Plan folder names are CamelCase** (e.g., `00001-UppercaseAllStringLiteralsInProgramCs`). `FindPlanFolder` normalizes by removing hyphens for comparison.
- **Plan IDs in sidebar are unpadded** — sidebar shows `#1`, not `#00001`. `GetPlanId` strips leading zeros.
- **Plan folders appear before `plan.yaml` is written.** Always wait for `plan.yaml` to exist, not just the folder.
- **Ivy is a server-rendered SPA over WebSocket.** Every UI interaction (click, fill) triggers a server round-trip. After filling a textarea, the server must re-render before buttons become enabled. Always wait for button enabled state before clicking.
- **Ivy framework `client.Redirect()` doesn't work reliably in headless Playwright.** After onboarding, the test polls the filesystem for config.yaml then forces `page.ReloadAsync()`.
- **Sidebar news cards can overlap navigation items.** The `SidebarNews` widget renders swipeable cards with `position: relative` that intercept pointer events on sidebar menu items. The `TENDRIL_E2E=1` env var suppresses news fetching. The `DashboardPage.ClickSidebarItem` also has a `force: true` fallback.
- **Duplicate "New Plan" buttons exist** (sidebar + empty state). Use `.First` on all button selectors.
- **The Jobs page uses a virtual grid (glide-data-grid).** Cells exist in the DOM but may not be visible (off-screen). Use `WaitForSelectorState.Attached` instead of `Visible` when asserting grid content.
- **CreatePlan invokes the coding agent** — takes 2-5+ minutes, not seconds.
- **`--find-available-port`** is required to avoid port conflicts between test runs.
- **Git pack files are read-only on Windows** — must clear read-only attributes before `Directory.Delete`.
- **After page reload, the sidebar may not immediately show new plans.** Retry with reloads.
- **The `--cli-log` file is a test instrument, not a job artifact.** It records every `tendril` CLI call the agent made (`{timestamp, command, exitCode, durationMs}` per line) and is asserted on by `CliLogAssertions`. Get its path from `_fixture.CliLogPath(name)`, which writes to `TestArtifactsDir` — a sibling of `TENDRIL_HOME`, never inside it. Only `promptware run --cli-log` produces one; app-launched jobs never do. Job logs are a separate thing entirely and live in `Jobs/` (see `LogAssertions`, which is lenient about missing logs under `dotnet run`).
- **Tests sharing the `E2E` collection must use unique plan descriptions.** Otherwise `FindPlanFolder` can match a folder from a previous test's plan.

## Troubleshooting test failures

### Step-by-step when a test fails

1. **Check the error message** — timeout errors include the last 20-30 lines of Tendril stdout and the Plans directory contents.
2. **Check screenshots** — on failure, screenshots are saved to `%TEMP%/tendril-e2e-*.png`. Look at sidebar state, dialog state, etc.
3. **Check job logs** — use `LogAssertions.GetJobLog(tendrilHome, planId)` to read the plan's job log from `<TendrilHome>/Jobs/`.
4. **Check the last reported status** — `GET /api/jobs/{jobId}` returns it, or read the `StatusMessage` column of the `Jobs` table in `tendril.db`. Job status is never written to disk.
5. **Check Tendril stdout/stderr** — accessible via `_fixture.Tendril.StdoutLines` / `StderrLines` in tests.

### Common failure patterns

- **"Tendril did not start within 60s" + MSBuild errors** — MSBuild lock conflict. Pre-build both projects and use `--no-build`. See Quick Start.
- **"InvalidOperationException: Service of type 'X' is not registered"** — A `UseService<T>()` call for a type that should come from a framework hook (e.g., `INavigator` → use `UseNavigation()` instead). This crashes the entire component tree. Check the screenshot — the sidebar will show an error panel instead of the menu.
- **Sidebar click timeout / "subtree intercepts pointer events"** — An overlay element (news card, tooltip, loading state) is blocking clicks. The `TENDRIL_E2E` env var suppresses news cards. If new overlays appear, add `force: true` fallback in `DashboardPage.ClickSidebarItem`.
- **"New Plan" button not found** — The sidebar failed to render (check for exceptions in Tendril stdout) or the button hasn't appeared yet (increase `WaitForAsync` timeout).
- **"Plan not visible in sidebar"** — The plan folder exists but the UI hasn't refreshed. The test retries with reloads for 120s. If it still fails, the sidebar rendering may have changed.
- **"plan.yaml not created"** — The coding agent failed or timed out. Check the job logs in `<TendrilHome>/Jobs/` for the agent's error output.
- **"state: Review not found"** — ExecutePlan didn't complete. Check if the agent errored, if verifications failed, or if the process was killed early.
- **"PR URL not found in plan.yaml"** — CreatePR promptware failed. Check GitHub auth (`gh auth status`) and fork permissions.
- **Port conflict in CleanupTests** — The `TendrilProcessFixture_CleansUpTempDirectories` test starts its own Tendril instance. If the main E2E fixture is still running, ports may collide even with `--find-available-port`. The test handles this gracefully via try/catch on `TimeoutException`.

## Promptware CLI status reporting

Promptwares report progress via `tendril job status`:

```
tendril job status <TendrilJobId> --message "Creating plan..." --plan-id 00001 --plan-title "My Plan"
```

The `<TendrilJobId>` value is provided as a firmware header in the agent's prompt. The command issues an HTTP `PUT api/jobs/{jobId}/status` to the running master instance (discovered via `$TENDRIL_HOME/.master`), which sets `job.StatusMessage` in memory; the Jobs table picks it up on the next cell-update tick. **Nothing is written to disk — there is no status file.**

### Verifiable status fields

- `message` — status text (e.g., "Reading plan...", "Verifying: DotnetBuild", "Creating PR...")
- `planId` — plan ID being processed
- `planTitle` — plan title being processed

### How to verify promptware status in tests

```csharp
// Check that job logs contain expected status messages
LogAssertions.AssertLogContains(tendrilHome, planId, "Creating plan...");

// Check the last reported status via the running server
// GET api/jobs/{jobId} -> { id, status, message }

// Check captured stdout for CLI invocations
var statusLines = fixture.Tendril.StdoutLines
    .Where(l => l.Contains("job status"))
    .ToList();
```

## What still needs testing

### Environment variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `E2E__Agent` | `claude` | Which coding agent to use (claude/codex/gemini/copilot/opencode) |
| `E2E__PlanExecutionTimeoutSeconds` | `600` | Max wait time for agent to complete a plan |
| `E2E__Headless` | `true` | Run browser headlessly (set `false` for visual debugging) |
| `E2E__SlowMo` | `0` | Milliseconds to slow down Playwright actions (useful for debugging) |
| `TENDRIL_E2E` | _(set by fixture)_ | Suppresses sidebar news cards and other non-essential UI for clean testing |

### Multi-agent coverage

Currently the default agent is `claude`. To verify Codex and Gemini work:

```bash
E2E__Agent=codex dotnet test --filter "FullyQualifiedName~AgentExecutionTests"
E2E__Agent=gemini dotnet test --filter "FullyQualifiedName~AgentExecutionTests"
```

Each agent has different CLI invocation patterns and auth requirements:
- **Claude**: `claude -p "..." --max-turns N` — needs `claude` CLI authenticated
- **Codex**: `codex -q "..."` — needs `codex` CLI authenticated
- **Gemini**: `gemini ...` — needs `~/.gemini/oauth_creds.json`

### Promptware CLI verification

We should add tests that verify promptwares properly call `tendril job status` with correct arguments:

1. **Status assertions** — after a job completes, `GET api/jobs/{jobId}` (or read the `Jobs` table) and verify the reported message, planId and planTitle.
2. **Log content assertions** — check job logs for expected status messages ("Creating plan...", "Reading plan...", "Verifying: DotnetBuild").
3. **Stdout capture** — search `fixture.Tendril.StdoutLines` for CLI invocation traces.

Only the *last* status message survives — `JobService.UpdateJobStatus` overwrites `job.StatusMessage` in place. To assert on the full progression, have the promptware record each step with `tendril job add-log`, which appends rather than overwrites, then read the job log back.

### Reactive wait strategy

Tests use three detection mechanisms (belt-and-suspenders):

1. **`PlanCreationWatcher`** / **`PlanStateWatcher`** — `FileSystemWatcher` + polling fallback. Detects plan.yaml creation or state changes. The watcher fires on filesystem events; polling at 2-3s intervals catches anything the watcher misses (Windows FSW is not 100% reliable with subdirectories).
2. **`StdoutMonitor.WaitForJobExit()`** — watches Tendril's captured stdout for `"Process exited with code"` or `"Process killed after timeout"` log lines. Detects job completion/failure within 500ms. Also fails fast on agent errors (`"Agent binary not found"`, etc.).
3. **Timeout** — `PlanExecutionTimeoutSeconds` (default 600s) is the outer safety net if both detection methods fail.

When adding new wait conditions, prefer `PlanStateWatcher` for filesystem state and `StdoutMonitor` for process-level events. Avoid bare `Task.Delay` or fixed sleep durations.

### Recommended test additions

- **PromptwareStatusTests** — verify that each promptware type (CreatePlan, ExecutePlan, CreatePr) reports planId, planTitle, and meaningful status messages
- **AgentErrorTests** — verify that agent failures (auth errors, network issues) produce clear error messages and don't leave orphaned processes
- **RecommendationTests** — verify that Tendril's recommendation engine (if applicable) produces output after plan execution
- **CommitVerificationTests** — after ExecutePlan, verify that the forked repo has new commits on the plan branch

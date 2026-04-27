# Ivy.Tendril.Test.End2End

Playwright-based E2E tests that drive Tendril as a black-box subprocess and verify the full lifecycle: onboarding, plan creation, agent execution, PR creation.

## Quick start

```bash
cd src/Ivy.Tendril.Test.End2End
dotnet build
dotnet test --verbosity normal
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
      logs/
  Promptwares/
    CreatePlan/Logs/{planId}.md
    ExecutePlan/Logs/{planId}.md
    CreatePr/Logs/{planId}.md
  jobs/
    {jobId}.status                   ← Status file (JSON)
  Logs/
    worktrees.log
```

## What the tests cover

| Test class | Tests | What it verifies |
|---|---|---|
| OnboardingTests | 1 | Full onboarding wizard: welcome → software check → agent selection → home setup → project setup → complete |
| VerificationTests | 4 | Directory structure, config.yaml, tendril.db, dashboard loads |
| PlanLifecycleTests | 2 | Plan creation via UI creates plan folder with plan.yaml; multiple plans appear in sidebar |
| AgentExecutionTests | 1 | Full lifecycle: CreatePlan → Execute → CreatePR (plan reaches ReadyForReview, PR URL in plan.yaml) |
| CleanupTests | 2 | Fixture teardown, GitHub fork deletion |

## Common gotchas

- **Plan folder names are CamelCase** (e.g., `00001-UppercaseAllStringLiteralsInProgramCs`). `FindPlanFolder` normalizes by removing hyphens for comparison.
- **Plan IDs in sidebar are unpadded** — sidebar shows `#1`, not `#00001`. `GetPlanId` strips leading zeros.
- **Plan folders appear before `plan.yaml` is written.** Always wait for `plan.yaml` to exist, not just the folder.
- **Ivy framework `client.Redirect()` doesn't work reliably in headless Playwright.** After onboarding, the test polls the filesystem for config.yaml then forces `page.ReloadAsync()`.
- **Duplicate "New Plan" buttons exist** (sidebar + empty state). Use `.First` on all button selectors.
- **CreatePlan invokes the coding agent** — takes 2-5+ minutes, not seconds.
- **`--find-available-port`** is required to avoid port conflicts between test runs.
- **Git pack files are read-only on Windows** — must clear read-only attributes before `Directory.Delete`.
- **After page reload, the sidebar may not immediately show new plans.** Retry with reloads.

## Troubleshooting test failures

### Step-by-step when a test fails

1. **Check the error message** — timeout errors include the last 20-30 lines of Tendril stdout and the Plans directory contents.
2. **Check screenshots** — on failure, screenshots are saved to `%TEMP%/tendril-e2e-*.png`. Look at sidebar state, dialog state, etc.
3. **Check promptware logs** — use `LogAssertions.GetJobLog(tendrilHome, planId)` to read the full agent execution log.
4. **Check status files** — look in `$TENDRIL_HOME/jobs/*.status` for the last reported job status (JSON with message, planId, planTitle).
5. **Check Tendril stdout/stderr** — accessible via `_fixture.Tendril.StdoutLines` / `StderrLines` in tests.

### Common failure patterns

- **"Plan not visible in sidebar"** — The plan folder exists but the UI hasn't refreshed. The test retries with reloads for 120s. If it still fails, the sidebar rendering may have changed.
- **"plan.yaml not created"** — The coding agent failed or timed out. Check promptware logs for the agent's error output.
- **"state: ReadyForReview not found"** — ExecutePlan didn't complete. Check if the agent errored, if verifications failed, or if the process was killed early.
- **"PR URL not found in plan.yaml"** — CreatePR promptware failed. Check GitHub auth (`gh auth status`) and fork permissions.

## Promptware CLI status reporting

Promptwares report progress via `tendril job status`:

```
tendril job status $TENDRIL_JOB_ID --message "Creating plan..." --plan-id 00001 --plan-title "My Plan"
```

This writes a JSON status file to `$TENDRIL_HOME/jobs/{jobId}.status`, which is polled by JobLauncher every 1 second and forwarded to the Jobs UI.

### Verifiable status fields

- `message` — status text (e.g., "Reading plan...", "Verifying: DotnetBuild", "Creating PR...")
- `planId` — plan ID being processed
- `planTitle` — plan title being processed

### How to verify promptware status in tests

```csharp
// Check that promptware logs contain expected status messages
LogAssertions.AssertLogContains(tendrilHome, planId, "Creating plan...");

// Check status file directly
var statusPath = Path.Combine(tendrilHome, "jobs", $"{jobId}.status");
var json = File.ReadAllText(statusPath);
// Parse and assert message, planId, planTitle

// Check captured stdout for CLI invocations
var statusLines = fixture.Tendril.StdoutLines
    .Where(l => l.Contains("job status"))
    .ToList();
```

## What still needs testing

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

1. **Status file assertions** — after a job completes, read `$TENDRIL_HOME/jobs/{jobId}.status` and verify it contains planId and planTitle.
2. **Log content assertions** — check promptware logs for expected status messages ("Creating plan...", "Reading plan...", "Verifying: DotnetBuild").
3. **Stdout capture** — search `fixture.Tendril.StdoutLines` for CLI invocation traces.

Consider adding a structured log in Tendril's `JobLauncher.RunStatusFilePoller()` that records every status update received, so tests can assert on the full status progression without relying on the transient status file.

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

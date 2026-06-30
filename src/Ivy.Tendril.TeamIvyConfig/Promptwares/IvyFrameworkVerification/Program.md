# IvyFrameworkVerification

Test and visually verify Ivy Framework UI changes by creating demo apps and running Playwright tests.

## Context

The firmware header contains:

- **PlanFolder** — path to the plan folder
- **ConfigPath** — absolute path to config.yaml
- **CurrentTime** — current UTC timestamp
- **VerificationDir** — path to write the verification report
- **ArtifactsDir** — path to store test artifacts (screenshots, videos, sample apps)
- **IvyFrameworkPath** — pre-resolved path to the Ivy Framework source (worktree or main repo). The launcher has already pre-built the frontend and Ivy.dll from this path. Use this for ProjectReference paths — do NOT rebuild the frontend yourself.

## Execution Steps

### 1. Read Plan

- Read `plan.yaml` from the plan folder
- Read the latest revision from `revisions/` to understand what changed
- Determine if the changes affect visual/UI behavior

**What counts as visual:** Any change that adds, removes, or modifies something that renders in the browser. This includes new icon mappings, new enum values that appear in the UI, new CSS classes, new component variants, new props with visual effects, etc. When in doubt, treat it as visual — a sample app that confirms "yes, this renders correctly" is always more valuable than skipping verification.

If the changes are strictly non-visual (docs, analyzers, refactoring, internal code-only fixes with no render path), write a report noting "No visual verification needed" and exit successfully.

### 2. Research

- Read `Memory/IvyFrameworkGotchas.md` for known API issues and workarounds
- Read `Memory/PlaywrightKnowledge-Index.md` for an overview of the split knowledge files
- Always read the critical files:
  - `Memory/PlaywrightKnowledge-Process.md` — mandatory for all tests
  - `Memory/PlaywrightKnowledge-Gotchas.md` — avoid known crash patterns
- Read additional files based on the feature being verified (identified in Step 1):
  - For new test projects or framework setup: `Memory/PlaywrightKnowledge-Framework.md`
  - For widget testing: `Memory/PlaywrightKnowledge-Widgets.md`
  - For locator/assertion work: `Memory/PlaywrightKnowledge-Locators.md`
  - For DOM/rendering issues: `Memory/PlaywrightKnowledge-DOM.md`
  - For test structure guidance: `Memory/PlaywrightKnowledge-Testing.md`
- Read the Ivy Framework AGENTS.md: `~/git/ivy/Ivy-Framework/AGENTS.md`
- Read relevant source code for the changed feature from `~/git/ivy/Ivy-Framework/src/`
- Read existing samples: `~/git/ivy/Ivy-Framework/src/Ivy.Samples.Shared/Apps/`

### 3. Verify Completeness

Check that required companion artifacts exist for the feature being verified:

1. **Identify the feature type** from the plan revision:
   - Widget (new or modified widget class)
   - Hook (new or modified hook)
   - Concept (new layout, form feature, navigation, etc.)
   - Bugfix/Refactor (internal change, no new public API)

2. **Sample App**: Search `~/git/ivy/Ivy-Framework/src/Ivy.Samples.Shared/Apps/` for files that demonstrate the feature:
   - Widgets → search by widget class name in `Apps/Widgets/`
   - Hooks → search by hook name (e.g. `UseQuery`) across all `Apps/`
   - Concepts → search by concept name across `Apps/Concepts/`
   - Bugfix/Refactor → skip (no sample expected)

3. **Documentation Page**: Search `~/git/ivy/Ivy-Framework/src/Ivy.Docs.Shared/Docs/` for documentation:
   - Widgets → `Docs/02_Widgets/`
   - Hooks → `Docs/03_Hooks/`
   - Concepts → `Docs/01_Onboarding/02_Concepts/`
   - Other → broad search across all `Docs/`
   - Bugfix/Refactor → skip (no doc expected)

4. If the plan's commits modified an existing Sample or Doc file, verify the changes are present in the worktree.

Record results for the report. For missing artifacts on new features, flag as a warning.

### 4. Create Sample Project

Create everything directly in `<ArtifactsDir>/sample/` so the plan folder is self-contained and runnable.

**Important: Use the `IvyFrameworkPath` firmware value** for the ProjectReference path. The launcher script has already pre-built the frontend and Ivy.dll from the correct source (worktree or main repo). Do NOT rebuild the frontend yourself — no `vp build`, no `npx vite build`, no `rm -rf obj/Debug`.

**`<ArtifactsDir>/sample/Sample.csproj`:**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="<IvyFrameworkPath>\src\Ivy\Ivy.csproj" />
    <ProjectReference Include="<IvyFrameworkPath>\src\Ivy.Analyser\Ivy.Analyser.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

**Note:** Always use the filename `Sample.csproj` (not `<FeatureName>.csproj`) to ensure reruns with "Suggested Changes" overwrite the same file instead of creating multiple .csproj files.

**`<ArtifactsDir>/sample/Program.cs`:**

```csharp
using Ivy;
using System.Reflection;

var server = new Server();
server.AddAppsFromAssembly(Assembly.GetExecutingAssembly());
server.UseAppShell();
await server.RunAsync();
```

### 5. Create Demo Apps

Create multiple `.cs` app files exercising the feature:

- **BasicApp** — Simplest usage, core functionality
- **PropsApp** — All props/configuration options with visible output
- **EventsApp** — All events with state feedback showing the event fired
- **IntegrationApp** — Feature combined with other Ivy widgets
- **EdgeCasesApp** — Empty values, large data, rapid interactions

Each app must:

- Inherit from `ViewBase` (NOT `AppBase`)
- Have `[App]` attribute with descriptive title and appropriate icon
- Show clear labels for what each section tests
- Display state changes visibly so Playwright can verify them

### 6. Build and Verify

From `<ArtifactsDir>/sample/`:

Before building, kill any leftover processes from previous runs that may lock DLLs (scoped to this plan's artifacts only):

```bash
powershell.exe -NoProfile -Command "\$dir = '<ArtifactsDir>' -replace '/','\'; Get-Process -ErrorAction SilentlyContinue | Where-Object { \$_.Path -and \$_.Path -like \"\$dir*\" } | ForEach-Object { Write-Host \"Killing \$(\$_.ProcessName) (PID \$(\$_.Id))\"; Stop-Process -Id \$_.Id -Force }; Start-Sleep -Milliseconds 2000"
```

**Important:** The `<ArtifactsDir>` firmware value uses forward slashes, but `Get-Process.Path` on Windows uses backslashes. The `-replace '/','\' ` normalization above is required for the `-like` filter to match.

Use the pre-flight build validation tool:

```bash
pwsh -NoProfile -File "<PromptwareDir>/Tools/Test-SampleBuild.ps1" -SampleProjectDir "<ArtifactsDir>/sample"
```

The tool runs `dotnet build` and `dotnet run --describe`, returning JSON with `success`, `apps`, and `errors` fields. If it fails, fix the **sample project code** (not the framework build) and re-run. The framework is already pre-built by the launcher.

If the tool is unavailable, fall back to:

```bash
dotnet build
dotnet run --describe
```

Fix any compilation errors. Iterate until build succeeds.

### 7. Create Playwright Tests

Create `<ArtifactsDir>/sample/.ivy/tests/` directory with:

**package.json** — minimal, with `@playwright/test` dependency

**IMPORTANT:** Screenshots must be written to `<ArtifactsDir>/screenshots/` (sibling to `sample/`), not inside `sample/`. Since `projectRoot` resolves to `<ArtifactsDir>/sample/`, use `path.resolve(projectRoot, '..', 'screenshots')` (single `..`) — NOT double `..` which goes above `<ArtifactsDir>`.

**Architecture: Single shared server.** All spec files share ONE `dotnet run` instance managed by Playwright's `webServer` config. This avoids spawning multiple server processes (which on Windows create unkillable process trees). The server hosts all apps on different routes — there is no need for separate server instances.

**playwright.config.ts:**

```typescript
import { defineConfig, devices } from '@playwright/test';
import path from 'path';

const projectRoot = path.resolve(__dirname, '..', '..');

export default defineConfig({
  testDir: '.',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  reporter: 'list',
  globalSetup: './global-setup.ts',
  globalTeardown: './global-teardown.ts',
  use: {
    baseURL: `https://localhost:${process.env.APP_PORT || '5123'}`,
    trace: 'retain-on-failure',
    ignoreHTTPSErrors: true,
    viewport: { width: 1920, height: 1920 },
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1920, height: 1920 },
      },
    },
  ],
});
```

**global-setup.ts** — spawns the server once before all tests:

```typescript
import { spawn, execSync } from 'child_process';
import net from 'net';
import https from 'https';
import fs from 'fs';
import path from 'path';

async function findFreePort(): Promise<number> {
  return new Promise((resolve) => {
    const server = net.createServer();
    server.listen(0, '127.0.0.1', () => {
      const addr = server.address();
      const port = typeof addr === 'string' ? 0 : addr?.port ?? 0;
      server.close(() => resolve(port));
    });
  });
}

async function waitForServer(url: string, timeoutMs: number): Promise<void> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      await new Promise<void>((resolve, reject) => {
        https.get(url, { rejectUnauthorized: false }, (res) => {
          if (res.statusCode === 200) resolve();
          else reject(new Error(`Status ${res.statusCode}`));
        }).on('error', reject);
      });
      return;
    } catch {
      await new Promise(r => setTimeout(r, 500));
    }
  }
  throw new Error(`Server at ${url} did not start within ${timeoutMs}ms`);
}

export default async function globalSetup() {
  const projectRoot = path.resolve(__dirname, '..', '..');
  const port = await findFreePort();

  const backendLogPath = path.resolve(projectRoot, '..', 'tests', 'backend.log');
  fs.mkdirSync(path.dirname(backendLogPath), { recursive: true });
  const backendStream = fs.createWriteStream(backendLogPath);

  const proc = spawn(
    'dotnet',
    ['run', '--no-build', '--', '--port', port.toString()],
    {
      cwd: projectRoot,
      shell: false,
      stdio: ['ignore', 'pipe', 'pipe'],
    }
  );

  proc.stdout?.pipe(backendStream);
  proc.stderr?.pipe(backendStream);

  // Write PID and port so teardown and tests can find them
  const stateFile = path.resolve(__dirname, '.server-state.json');
  fs.writeFileSync(stateFile, JSON.stringify({ pid: proc.pid, port }));

  // Export port for playwright config
  process.env.APP_PORT = port.toString();

  await waitForServer(`https://localhost:${port}`, 30000);

  console.log(`Server started on port ${port} (PID ${proc.pid})`);
}
```

**global-teardown.ts** — kills the server after all tests, with fallback by name:

```typescript
import { execSync } from 'child_process';
import fs from 'fs';
import path from 'path';

export default async function globalTeardown() {
  const stateFile = path.resolve(__dirname, '.server-state.json');

  if (!fs.existsSync(stateFile)) {
    console.warn('No .server-state.json found — skipping teardown');
    return;
  }

  const { pid } = JSON.parse(fs.readFileSync(stateFile, 'utf-8'));
  fs.unlinkSync(stateFile);

  if (process.platform === 'win32') {
    // Kill the process tree. /T kills child processes too.
    try {
      execSync(`taskkill /pid ${pid} /F /T`, { stdio: 'pipe' });
      console.log(`Killed server process tree (PID ${pid})`);
    } catch (e: any) {
      // PID may already be gone — try name-based fallback scoped to plan artifacts
      console.warn(`taskkill /pid ${pid} failed: ${e.stderr?.toString().trim()}`);
    }

    // Fallback: kill any remaining Sample.exe from this project's bin dir
    const projectRoot = path.resolve(__dirname, '..', '..');
    try {
      const result = execSync(
        `powershell.exe -NoProfile -Command "` +
        `$dir = '${projectRoot.replace(/\//g, '\\\\')}' -replace '/','\\\\';" +
        `Get-Process -Name Sample -ErrorAction SilentlyContinue | ` +
        `Where-Object { \\$_.Path -and \\$_.Path -like \\"$dir*\\" } | ` +
        `ForEach-Object { Write-Host \\"Killing \\$($_.ProcessName) (PID \\$($_.Id))\\"; Stop-Process -Id \\$_.Id -Force }"`,
        { stdio: 'pipe' }
      );
      if (result.toString().trim()) console.log(result.toString().trim());
    } catch {
      // Best effort
    }
  } else {
    try {
      process.kill(pid, 'SIGKILL');
      console.log(`Killed server process (PID ${pid})`);
    } catch {
      // Already exited
    }
  }
}
```

**test-utils.ts** — screenshot helper (process management is now in global-setup/teardown):

```typescript
import { Page } from '@playwright/test';
import path from 'path';
import fs from 'fs';

/**
 * Take screenshot only if page has meaningful content.
 * Skips screenshots of empty/blank pages.
 */
export async function takeScreenshotIfNotEmpty(page: Page, screenshotName: string): Promise<boolean> {
  const projectRoot = process.cwd().replace(/[/\\]\.ivy[/\\]tests$/, '');
  const screenshotsDir = path.resolve(projectRoot, '..', 'screenshots');
  fs.mkdirSync(screenshotsDir, { recursive: true });
  const fullPath = path.join(screenshotsDir, screenshotName);

  const visibleText = await page.locator('body').innerText().catch(() => '');
  const elementCount = await page.locator('body *').count().catch(() => 0);

  if (visibleText.trim().length > 20 || elementCount > 5) {
    await page.screenshot({ path: fullPath, fullPage: true });
    return true;
  }

  console.log(`Skipped screenshot ${screenshotName} - page has no meaningful content`);
  return false;
}

/**
 * Read the server port from the state file written by global-setup.
 */
export function getServerPort(): number {
  const stateFile = path.resolve(__dirname, '.server-state.json');
  const { port } = JSON.parse(fs.readFileSync(stateFile, 'utf-8'));
  return port;
}
```

**One `.spec.ts` per app** (all sharing the same server instance):

- Import `takeScreenshotIfNotEmpty` and `getServerPort` from `./test-utils`
- Read port from `getServerPort()` (or use `process.env.APP_PORT`)
- Set `test.setTimeout(60000)` (60s) to catch hung tests
- Test each app at `https://localhost:<port>/<app-id>?shell=false`
- Take screenshots to `<ArtifactsDir>/screenshots/` with descriptive names. **Before taking each screenshot, check if the page has meaningful content (visible text > 20 chars or > 5 visible elements). Skip screenshots of empty/blank pages** — these add no verification value.
- Capture browser console logs → `<ArtifactsDir>/tests/console.log`
- Do NOT spawn any `dotnet run` inside spec files — the server is already running via `globalSetup`

**Test coverage must verify:**

1. Feature renders correctly (screenshots)
2. All props produce expected visual output
3. All events fire correctly (state feedback)
4. Feature integrates with other widgets
5. No console errors or warnings
6. No backend errors or exceptions

**Code patterns (refer to PlaywrightKnowledge-Index.md for specific files):**

- Use `getByText()`, `getByRole()` locators
- Use `.first()` when multiple matches possible
- Use `waitForTimeout(500)` after interactions
- Resolve project root: `process.cwd().replace(/[/\\]\.ivy[/\\]tests$/, "")`
- Use `takeScreenshotIfNotEmpty()` instead of raw `page.screenshot()` — skips blank pages
- Do NOT use `shell: true` or spawn `dotnet run` in spec files

**Spec file pattern:**

```typescript
import { test, expect } from '@playwright/test';
import path from 'path';
import fs from 'fs';
import { takeScreenshotIfNotEmpty, getServerPort } from './test-utils';

test.describe('Feature Tests', () => {
  const projectRoot = process.cwd().replace(/[/\\]\.ivy[/\\]tests$/, '');
  const consoleLogPath = path.resolve(projectRoot, '..', 'tests', 'console.log');
  const consoleLogs: string[] = [];
  let port: number;

  test.setTimeout(60000);

  test.beforeAll(async () => {
    port = getServerPort();
  });

  test.afterAll(() => {
    fs.mkdirSync(path.dirname(consoleLogPath), { recursive: true });
    fs.appendFileSync(consoleLogPath, consoleLogs.join('\n') + '\n');
  });

  test('should render feature correctly', async ({ page }) => {
    page.on('console', (msg) => consoleLogs.push(`[${msg.type()}] ${msg.text()}`));

    await page.goto(`https://localhost:${port}/app-id?shell=false`);
    await page.waitForTimeout(500);

    // Verify content...
    await takeScreenshotIfNotEmpty(page, 'feature-screenshot.png');

    const errors = consoleLogs.filter(log => log.includes('[error]'));
    expect(errors).toHaveLength(0);
  });
});
```

### 8. Install & Run Tests

```bash
cd <ArtifactsDir>/sample/.ivy/tests
npm install
npx playwright install chromium
npx playwright test
```

### 8.5. Post-Test Cleanup (Mandatory)

Even if tests pass, kill this plan's sample processes to ensure clean state. This step MUST succeed before proceeding — locked DLLs prevent rebuilds and leave zombie processes.

**Step A — Kill by name, scoped to this plan's artifacts:**

```bash
powershell.exe -NoProfile -Command "\$dir = '<ArtifactsDir>' -replace '/','\'; Get-Process -Name Sample -ErrorAction SilentlyContinue | Where-Object { \$_.Path -and \$_.Path -like \"\$dir*\" } | ForEach-Object { Write-Host \"Killing \$(\$_.ProcessName) (PID \$(\$_.Id))\"; Stop-Process -Id \$_.Id -Force }; Start-Sleep -Milliseconds 2000"
```

**Step B — Verify no processes remain:**

```bash
powershell.exe -NoProfile -Command "\$dir = '<ArtifactsDir>' -replace '/','\'; \$remaining = Get-Process -Name Sample -ErrorAction SilentlyContinue | Where-Object { \$_.Path -and \$_.Path -like \"\$dir*\" }; if (\$remaining) { Write-Host \"WARNING: \$(\$remaining.Count) processes still alive:\"; \$remaining | ForEach-Object { Write-Host \"  \$(\$_.ProcessName) PID=\$(\$_.Id) Path=\$(\$_.Path)\" }; exit 1 } else { Write-Host 'All sample processes cleaned up.' }"
```

**Step C — If Step B fails, escalate with taskkill /F /T on each PID:**

```bash
powershell.exe -NoProfile -Command "\$dir = '<ArtifactsDir>' -replace '/','\'; Get-Process -Name Sample -ErrorAction SilentlyContinue | Where-Object { \$_.Path -and \$_.Path -like \"\$dir*\" } | ForEach-Object { \$pid = \$_.Id; Write-Host \"Force-killing PID \$pid with /T\"; cmd /c \"taskkill /F /T /PID \$pid\" }; Start-Sleep -Milliseconds 3000"
```

If processes still survive after Step C, note this in the verification report as a test infrastructure issue but continue with report writing.

### 9. Fix Loop (up to 10 rounds)

If tests fail, logs have errors, or screenshots show issues:

1. Analyze failures — categorize as:
   - **Test code issue** → fix `.spec.ts`
   - **Demo app issue** → fix `.cs` files
   - **Framework bug** → document in report
2. Apply fixes and re-run
3. Track each fix round

### 10. Verify Artifacts

Everything is already in place under `<ArtifactsDir>/`:

- `sample/` — `.csproj`, `.cs` files, `.ivy/tests/` (runnable project)
- `screenshots/` — Playwright screenshots
- `tests/` — `console.log`, `backend.log`

Confirm all expected files exist before writing the report.

### 11. Write Verification Report

Write to `<VerificationDir>/IvyFrameworkVerification.md`:

```markdown
# IvyFrameworkVerification

- **Plan:** <planId> — <title>
- **Date:** <CurrentTime>
- **Result:** Pass / Fail
- **Test Project:** <path to temp project>

## What was tested

<description of what was verified>

## Completeness

| Artifact | Status | Path |
|----------|--------|------|
| Sample App | Found/Missing/N/A | path or N/A |
| Documentation | Found/Missing/N/A | path or N/A |

## Props Tested

| Prop | Status | Notes |
|------|--------|-------|

## Events Tested

| Event | Status | Notes |
|-------|--------|-------|

## Visual Quality

<assessment of appearance and UX>

## Log Cleanliness

### Frontend Console
<clean / issues found>

### Backend Logs
<clean / issues found>

## Artifacts

- Screenshots: <list>
- Sample app: <path>

## Issues Found

| Issue | Severity | Area | Details |
|-------|----------|------|---------|

## Recommendations

<any suggestions>
```

### Rules

- **Fail-fast on missing permissions:** Before doing any work, verify you can use Write/Edit tools by checking your allowed tools. If Write is unavailable, immediately run `exit 1` via Bash with a message: "ERROR: Write tool not available. IvyFrameworkVerification requires Write permission to create sample projects and reports." Do NOT output a polite request for permission — you are in non-interactive mode.
- Do NOT modify any source code in the Ivy Framework repos — this is a verification step only
- If verification fails, describe the failure clearly in the report
- Always produce a report, even for non-visual changes (just note it was skipped)
- Always read Memory files before creating test code — they contain critical gotchas
- Screenshots are evidence — take many, with descriptive names
- Keep demo apps focused — each tests a specific aspect

---
searchHints:
  - release notes
  - changelog
  - version history
  - updates
  - what's new
icon: ScrollText
---

# Release Notes

<Ingress>
Version history, new features, improvements, and bug fixes for each Tendril release.
</Ingress>

## 1.0.39 (2026-05-28)

### Features

- **Gemini agent provider** — Added Gemini CLI (`gemini`) as a supported coding agent, with full health check, authentication, and session cost tracking.
- **Tunnel support** — Remote access via Cloudflare tunnels with QR code in Settings, automatic server-ready detection, and routable-before-connected checks.
- **Agent test dialog** — New Test Agent button in Settings that auto-runs install, auth, and model checks for all configured agents.
- **Model-per-profile selection** — Choose specific models per effort profile (deep/balanced/quick) in Coding Agent settings.
- **Per-provider model catalogs** — Replaced global `models.yaml` with per-provider catalogs and a `tendril models` CLI command.
- **`tendril update` command** — Self-update with Photino GUI updater.
- **Plan template injection** — Plan templates are injected into firmware; actual model used is tracked per job.
- **Human-readable tool titles** — Description field on ToolCallWire for clearer Agent Output display.
- **Sandboxed agent file access** — Agents get writable access to TENDRIL_HOME, plans, and promptware folders.
- **`--search` option for plan list** — Filter plans by search term from the CLI.
- **AgentApp with system prompt** — Beta agent chat app with injected Tendril system prompt.
- **Create Plan from wallpaper** — New Plan button on the wallpaper opens the CreatePlanDialog directly.
- **Copy all Details button** — Copy full job debug details to clipboard in the Job Debug Sheet.
- **Newsletter view extraction** — Shared newsletter component with better error reporting.

### Improvements

- **Settings split** — General settings split into Coding Agent, Plans, and Appearance tabs.
- **PlansApp → DraftsApp rename** — Sidebar badge and navigation updated to match.
- **Coding agent settings layout** — Improved layout with display names and default model handling for all providers.
- **CLI polish** — Clean console formatter, `--help` without starting server, clean error for unknown commands, doctor output formatting.
- **AgentOutputView polish** — Tool cards with no-wrap output, cleaner titles, uniform spacing, hidden status on complete.
- **Process view improvements** — Equal-width buttons, gray pulse, semantic color tokens for dark mode, deduplicated hook.
- **TendrilProcessView widget** — Added to solution with dark mode support via semantic color tokens.
- **Install script improvements** — Verified git execution, prepended .NET 10 to PATH, cleaner scripts.
- **Dependency security** — Pinned dependency version ranges to exact versions to prevent hijack and confusion attacks.
- **Validate base branch** — Prevents adding projects with invalid base branches or invalid local repositories.
- **Raw agent output** — Written to `.raw.jsonl` instead of EventWire format for better debugging.
- **Copilot improvements** — Switched to stdin prompt for Windows command-line length limit, fallback to `gh copilot` when standalone binary not on PATH, parse updated JSON format.
- **CodeBlock widget** — Agent output and resolution uses CodeBlock instead of raw Markdown.
- **Service organization** — Services refactored into subdirectories; status constants extracted.

### Bug Fixes

- Fixed process view showing swapped updating/executing plan counts.
- Fixed onboarding path resolution when tendrilHome parameter is empty.
- Fixed onboarding infinite "Setting up agent" loading screen.
- Fixed database migration 10→11 upgrade by making Migration 11 idempotent.
- Fixed backslashes in .csproj files and onboarding Promptwares path lookup.
- Fixed Copilot process hangs with 5s STDIN timeout.
- Fixed missing ResolveCommandShim call in PromptwareRunner.
- Fixed command-line length limit when launching Gemini
- Fixed Codex `item.updated` events emitting UnknownEvent.
- Fixed default models for Copilot and Codex profiles in new installations.
- Fixed sidebar badge key from "plans" to "drafts" after rename.
- Fixed duplicate headers and styling in Add Project dialog.
- Fixed wrong edit project dialog index mismatch after adding project.
- Fixed model dropdown not showing Default option.
- Fixed Windows PTY command resolution to .cmd extension.
- Fixed null models during agent switch.
- Fixed "undefined:" prefix in job status messages.
- Fixed duplicate project name blocking onboarding.
- Fixed onboarding raw agent output parsing to EventWire in real-time.
- Fixed Windows app launch extra window and missing taskbar icon.
- Fixed AgentOutputView tool results not rendering.
- Fixed Claude Code tool result parsing from user messages.
- Fixed cloudflared 502 by reading actual server address.
- Fixed OpenCode `model: default` to skip --model flag.
- Fixed OpenCode intermediate step_finish events in output view.

## 1.0.35 (2026-05-20)

### Features

- **Native OS toast notifications** — Desktop notifications for plan completions, failures, and other events, with a dedicated Notifications tab in Settings.
- **Taskbar badge** — Active job count displayed in the desktop taskbar badge for at-a-glance status.
- **Wizard-based Add Project** — New project setup now uses a guided wizard flow matching onboarding, with skip capability for experienced users.
- **Move-verification CLI command** — Reorder verifications via `tendril project move-verification` with ordering instructions.
- **Redesigned onboarding** — "Your First Project" is now a 3-step flow with fresh project setup, progressive feedback, and newsletter signup on completion.
- **CLI CRUD commands** — Full CRUD for verifications and projects via the CLI (`tendril project get`, `tendril verification add/remove/move`).
- **Plan commit sync** — Synchronize plan commits on demand via the Synchronize button in Review.
- **ReviewAction CRUD UI** — Configure review actions directly from Settings and onboarding.
- **`tendril reset` command** — Reset Tendril state via the CLI.
- **`tendril report-bug` command** — File bug reports with system context directly from the CLI.
- **`promptware read-memory` command** — Inspect promptware memory from the CLI.
- **Draft mode for PR creation** — Option to create PRs as GitHub drafts.
- **Recommendations Accept/Decline** — Accept or decline recommendations directly in the Review app, with filtering by Completed plans.
- **Git tab: Worktrees tile** — Shows parent repo details and groups commits under worktree sections.
- **Keep worktrees alive for Failed plans** — Failed plan worktrees are preserved for debugging instead of being cleaned up.
- **OpenCode agent provider** — Added OpenCode as a supported coding agent.
- **Copilot CLI agent provider** — Added GitHub Copilot CLI as a supported coding agent.
- **`--plans-dir` CLI flag** — Override the plans directory for E2E testing and custom setups.
- **TendrilProcessView widget** — External widget for visualizing Tendril processes.

### Improvements

- **Git tab polish** — Icons on section headers and empty state, hierarchical tree with color indicators for changed files.
- **Changes tab stability** — Fixed blinking during 30s background revalidation, expand-by-default behavior, and full-width layout.
- **Review tab cleanup** — Empty Artifacts and Recommendations tabs are now hidden; plan views use article typography.
- **Commit messages simplified** — Removed plan ID prefix from commit message instructions for cleaner git history.
- **Import Issues from GitHub polished** — Improved UX for the GitHub issue import flow.
- **Window sizing** — Updated default window dimensions to work properly on macOS Retina displays, with minimum size enforced.
- **RetryPlan improvements** — Appends fix sections to existing summary, clarifies multi-repo worktree setup, streams raw log to disk.
- **VerbosityService removed** — Replaced with standard ILogger levels for simpler logging configuration.
- **ServiceRegistration extraction** — Service registrations moved from TendrilServer into a dedicated `ServiceRegistration.cs`.
- **Onboarding code health** — Extracted helpers, added AgentOnboardingInfo, primary constructors, and improved UX copy.
- **Promptware tool permissions** — Updated default tool permissions for safer agent execution.
- **CLI documentation restructured** — Comprehensive rewrite of CLI reference with updated command syntax and examples.
- **Full-width markdown in plan views** — Scrollable content with max-width constraint for readability.
- **Responsive Jobs table** — Large density on tablet, Medium on desktop for better space usage.
- **Remove generate verifications** — Removed from project edit dialog in favor of CLI-based verification management.
- **Framework exceptions hidden** — Framework-internal exceptions no longer surface as user-facing notifications.

### Bug Fixes

- Fixed reset-to-draft not updating UI immediately after confirmation.
- Fixed project verification order not preserved in onboarding review step.
- Fixed onboarding steps stuck after progress completes.
- Fixed "No summary available" flash when opening a plan in Review.
- Fixed Changes tab blinking every 30s during background revalidation.
- Fixed test parallelism contaminating TeamIvyConfig `config.yaml`.
- Fixed commit hashes stored as short hashes in syncer — now stores full hashes and refreshes UI after sync.
- Fixed commits lost across RetryPlan executions.
- Fixed review action command paths to use quoted PowerShell syntax.
- Fixed error notification when canceling dialogs with ESC.
- Fixed subfolder casing migration and broken cleanup tests.
- Fixed `plan.yaml` corruption during UpdatePlan execution.
- Fixed duplicate content in Agent Output during live streaming.
- Fixed job output rendered twice when job completes.
- Fixed PromptwareRoot resolution bug causing missing promptwares.
- Fixed Update Available toast spacing and position.
- Fixed incomplete "You have ." message in WallpaperApp.
- Fixed missing application window icon by updating resource names.
- Fixed `gh auth status` failing with multiple GitHub accounts.
- Fixed output sheet showing empty panel for completed jobs.
- Fixed bogus ReportedPlanId when no matching plan folder exists.
- Fixed Jobs table sorting to show newest jobs first.
- Fixed completed jobs filtered out on restart.
- Fixed delegated verification invocation syntax causing IvyFrameworkVerification failures.
- Fixed onboarding Complete Setup button hanging indefinitely.
- Fixed infinite hang in background service startup.
- Fixed tab name scoping issue in Review.

## 1.0.22 (2026-04-27)

### Improvements

- **GitResult\<T\> error handling** — Introduced a typed `GitResult<T>` return type across GitService for consistent, explicit error handling instead of exceptions.
- **DashboardRepository extraction** — Extracted `GetDashboardData` into a dedicated DashboardRepository, separating data access from business logic.
- **ISessionParser interface** — Extracted session parsing behind an `ISessionParser` interface for testability and future parser variants.
- **PlanYamlRepairService extraction** — Moved plan YAML repair logic and worktree removal into dedicated services (`PlanYamlRepairService`, `WorktreeCleanupService`).
- **AppShellRouter extraction** — Extracted routing logic from `OpenApp` into a dedicated `AppShellRouter` class.
- **IDoctorCheck implementations** — Refactored doctor diagnostic checks into individual `IDoctorCheck` classes for extensibility.
- **Centralized MCP authentication** — Consolidated MCP tool authentication into a single service.
- **BackgroundServiceActivator guard** — Added detection and recovery for silent background process death.
- **IDisposable pattern in PlanDatabaseService** — Proper resource cleanup for database connections.
- **Async SoftwareCheckStepView** — Replaced blocking `.Result` calls with `await` for responsive UI during health checks.
- **Comprehensive code health pass** — Reduced cyclomatic complexity across ContentView, PlanController, PlanTools, ConfigService, GithubService, JobLauncher, ModelPricingService, TendrilAppShell, and GetPromptDisplay via method extraction and data-driven refactors.
- **Test infrastructure** — Added `TempDirectoryFixture`, `ConfigServiceFixture`, `DatabaseFixture`, and `IClassFixture` patterns; expanded test coverage for GitService, PlanValidationService, JobLauncher, and PlanId allocation.
- **Dashboard 7-day window** — Status counts and project counts on the dashboard now filter to the last 7 days.

### Bug Fixes

- Fixed PlanId allocation race condition by centralizing allocation in JobService.
- Fixed `ModifyPlanEndpoint` returning incorrect result types.
- Fixed `DashboardRepository` logger type mismatch.
- Fixed GitHub issue auto-close by moving `Closes` reference after body truncation.
- Fixed race condition in `InboxWatcherService` file rename.
- Fixed nullable parameter handling in `IsValidCommitHash`.
- Fixed exception handling in cost tracking task.
- Fixed service provider access in `Program.cs`.
- Fixed `TabState` reference in `AppShellRouter` and handler method access modifiers.
- Removed repository concurrency blocking from JobService.
- Removed `DashboardLoggerAdapter` — uses logger directly.
- Added logging to swallowed exceptions across services.
- Fixed CI/Docker: Node.js v22, proper `IvySource` handling, removed stale Ivy-Framework references.

## 1.0.14 (2026-04-10)

### Features

- **Job priority queue** — Plans are now executed in priority order. Bug-level plans run before NiceToHave, ensuring critical fixes land first.
- **Import Issues from GitHub** — Import existing GitHub issues directly into Tendril as draft plans via the new Import dialog.
- **Multi-project plan creation** — The Create Plan dialog now supports selecting multiple projects, aggregating their repos into a single plan.
- **WorktreeLifecycleLogger** — Centralized audit trail for worktree create, cleanup, and failure events across PlanReaderService, WorktreeCleanupService, and JobService.
- **Advanced Settings tab** — New tab in Setup for configuring lower-level options.

### Improvements

- **Progressive health check feedback** — Health checks now stream individual results as they complete instead of waiting for all checks to finish.
- **PR status stored in SQLite** — PR merge status is now cached in the local database with a background sync service, reducing GitHub API calls.
- **PlanWatcher simplified** — Replaced heavy FileSystemWatcher usage with a simpler approach to avoid buffer overflow from worktree churn.
- **Worktree diagnostic logging** — Added fail-fast checks for missing `.git` files and improved error messages for worktree creation failures.
- **Recursive worktree artifact detection** — ExecutePlan now detects and removes nested worktree artifacts left in the Plans directory from prior runs.
- **Defensive dictionary access** — MakeSoftwareRow uses `GetValueOrDefault` to prevent KeyNotFoundException in edge cases.

### Bug Fixes

- Fixed Gemini health check opening browser windows during authentication.
- Fixed `anyAgentHealthy` check to use installation status for Gemini agent.
- Fixed ConfigService constructor testability.
- Fixed YAML parsing errors in `recommendations.yaml`.
- Removed redundant Watch Remove from `Ivy.Tendril.csproj`.
- Removed unused `_prStatusCache` from GithubService.

## 1.0.12 (2026-04-10)

### Features

- **Multi-agent support** — Tendril now supports multiple coding agents (Claude, Codex, Gemini) with configurable profiles (deep, balanced, quick) per agent.
- **Windows installer** — New `install.ps1` script for streamlined Windows installation.
- **Doctor command** — Run `tendril doctor` to diagnose configuration and environment issues.

### Improvements

- **Documentation overhaul** — Comprehensive rewrite of all Tendril documentation with improved structure, examples, and onboarding flow.
- **Onboarding wizard polish** — Improved UI, copy, and step layout for the first-run experience.
- **Stack-agnostic promptwares** — Removed stack-specific references from ExecutePlan, CreatePlan, and other promptwares to support any tech stack via `config.yaml` verifications.
- **Replaced FolderInput with TextInput** — Simplified path input across Tendril apps.

### Bug Fixes

- Fixed `TENDRIL_HOME` environment variable handling in tests.
- Added error handling to `PlatformHelper.OpenInTerminal` and `OpenInFileManager`.
- Added `File.Exists` check before reading `plan.yaml` in PlanReaderService.

## 1.0.9 (2026-04-09)

### Features

- **Stable NuGet releases** — Tendril now publishes stable versioned NuGet packages using `Directory.Build.props` for centralized versioning.
- **SQLite database** — Local data storage for plans, jobs, and PR status with migration support.
- **Recommendations system** — Plans can now generate follow-up recommendations that are surfaced in the Recommendations app.
- **Plan lifecycle management** — Full plan state machine: Draft, Approved, Executing, Review, Completed, Failed, with automatic transitions.

### Improvements

- **Cost tracking** — Per-job cost and token tracking with dashboard visualization by project and promptware type.
- **Comprehensive job status enum** — String conversion support for all job statuses.
- **Error handling improvements** — Duplicate migration version detection and FTS5 error handling.

## 1.0.0 (2026-04-03)

### Features

- **Initial release** of Tendril plan management system.
- **Plan apps** — Dashboard, Review, Drafts, Jobs, Icebox, Pull Requests, Recommendations, and Trash views.
- **Promptwares** — CreatePlan, ExecutePlan, CreatePr, UpdatePlan, SplitPlan, ExpandPlan, and CreateIssue.
- **Cross-platform support** — macOS and Windows with automatic platform detection.
- **Worktree-based execution** — Plans execute in isolated git worktrees to keep the main repo clean.
- **Configurable verifications** — Build, Test, Format, Lint, and CheckResult (with stack-specific variants like DotnetBuild, NpmTest).
- **GitHub integration** — Automatic PR creation, status tracking, and merge detection.
- **Keyboard shortcuts** — `Ctrl+Alt+D` for new drafts, with customizable bindings.

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

## 1.1.35 (2026-08-28)

### Features

- **Share Mode Tunnel & External Sharing `[Beta]`** - Securely share plan and draft links externally over Cloudflare tunnels with automatic share URL generation and copy actions, gated behind the beta flag (`beta: true` in settings or `TENDRIL_BETA`).
- **Session Protection for Shared Mode** - Added password session protection with Argon2 hashing in Settings under a unified "Security & Tunneling" section.
- **Anonymous Reviewer Personas** - Generated friendly anonymous personas with initialed avatars for external collaborators reviewing shared plans.
- **Draft & Plan Diff Inline Comments** - Added real-time inline reviewer commenting on diff chunks in Review mode (`DraftDiffCommentService`) with a dedicated "Request Changes" action and badge counts.
- **Draft Text Selection Annotations** - Added text selection highlighting and popovers anchored to character offsets in `DraftMarkdown` (`DraftAnnotationService`).
- **Team Configuration Vault `[Beta]`** - Introduced centralized team configuration sync backed by Git repositories (`VaultService`, accessible under beta flag), allowing teams to create, connect, import, and push project configs to remote vaults with automated secret sanitization (`VaultSecretSanitizer`).

### Improvements

- **Job Provenance & Profile Recording** - Recorded execution profiles per job and structured cost sheet provenance as facts.
- **Beta Feature Isolation** - Ensured Share buttons, tunnel controls, and vault configurations are cleanly isolated behind beta flags in both UI and command layers.

### Bug Fixes

- **Windows Desktop Packaging** - Fixed packaging failure by using junk-path zip extraction for bundled `ivy-agent.exe` on Windows x64 and arm64 builds.

## 1.1.34 (2026-08-25)

### Features

- **Embedded PTY Terminal for Review Actions** - Review actions now execute in a responsive embedded terminal tab powered by Xterm (`ReviewActionApp`) instead of launching external terminal windows.
- **Bundled Ivy Agent CLI** - Bundled the standalone `ivy-agent` executable directly with the Tendril application installer, removing manual installation requirements.
- **Bring Your Own LLM (BYO LLM) & Model Catalogs** - Added provider catalogs and model selectors for BYO LLM configurations and Ivy Proxy across onboarding and settings, with support for Gemini 3.7 Flash, Claude models, and OpenAI reasoning models.
- **Model Reasoning Effort Selection** - Introduced effort level pickers (low, medium, high) for supported reasoning models in coding agent profile settings.
- **Custom MCP Servers and Agent Skills** - Added support for importing MCP servers and custom skills directly from Git repositories, remote URLs, and local file paths, complete with management UI and validation.
- **Token and Cost Breakdown Sheet** - Introduced interactive token usage and cost breakdown sheets accessible directly from Job cost cells in the Jobs table.
- **Multi-Repo Worktree Organization** - Structured plan worktrees under `Worktrees/<owner>/<repo>` paths to support multi-repo setups and complex project layouts.
- **Keyboard Navigation & Tab Management** - Added `Cmd+W` / `Ctrl+W` shortcut support to close active tabs in standalone Tendril, and polished macOS Command (`⌘`) shortcut indicators across dialogs.

### Improvements

- **Draft & Review Performance Optimization** - Dramatically reduced plan switching latency and tab-switch overhead in both Review and Drafts apps.
- **Jobs DataTable Scalability** - Optimized DataTable rendering and data sync to seamlessly handle over 100+ active and historical jobs without UI stutter.
- **Chat Queue and Status Indicators** - Redesigned Chat queued messages panel with inline controls, and added real-time generating status badges in the sidebar.
- **Draft Annotations Anchoring** - Anchored selection popovers and toolbar highlights to text character offsets in DraftMarkdown to prevent drift when scrolling.
- **Worktree Base Branch Display** - Displayed upstream base branch fork points in the Review Git tab and added branch tracking in the Pull Requests overview.
- **Desktop Notification Toast Suppression** - Suppressed redundant in-app toast alerts when native operating system desktop notifications are displayed.
- **Settings UI Redesign** - Refactored project settings layout with project color swatches, collapsible custom skills/MCP cards, and standardized button sizing.

### Bug Fixes

- **Worktree Unpushed Commit Protection** - Stopped the worktree reaper from orphaning or deleting unpushed plan commits during background cleanup.
- **Tool Call Header Stickiness** - Ensured agent tool call titles stick to the top of the viewport during long stream outputs.
- **False Job Failure on Recovered Tool Errors** - Prevented Antigravity jobs from falsely failing when the agent successfully self-heals after an initial tool error.
- **GitHub PR URL Case Insensitivity** - Supported case-insensitive repository URLs (`Https://`, `Git@`, etc.) during GitHub import and PR operations.
- **Markdown Link Polisher Span Preservation** - Fixed plan link replacement in markdown polisher from corrupting nested plan spans.
- **Onboarding Flow Stability** - Fixed onboarding hang when an agent installation fails or when required binaries are temporarily missing.

## 1.1.19 (2026-07-28)

### Features

- **Claude Opus 5 Support** - Added `Claude Opus 5` (`claude-opus-5`) model support to the Claude catalog with refreshed pricing metadata.
- **Bulk Job Cancellation** - Introduced "Stop All Jobs" and "Stop All Queued" header actions in `JobsApp` (`IJobService.StopAllJobs` and `StopQueuedJobs`) for bulk job management.
- **Promptware Memory Pruning** - Added `promptware delete-memory` CLI command and firmware capability, allowing promptwares to delete obsolete memory files.
- **Memory Reference Resolution** - Automatically resolve memory references in `read-memory` CLI command instead of erroring when referenced notes are requested.
- **Configurable Ollama Agent URL** - Added configurable base URL option (`--url`) for local Ollama agent endpoints.
- **Import Issues Capacity** - Expanded the Import Issues dialog limit from 100 to 1,000 issues with truncation warnings when reaching ceilings.
- **In-Flight Job State Persistence** - Persisted active in-flight jobs in SQLite database so job status updates survive master process restarts.
- **Windows Automatic CLI PATH Setup** - Automatically create `tendril.cmd` wrappers and register the application directory in the Windows User PATH on app launch and Velopack installer hooks.

### Improvements

- **Tunnel Auto-Refresh Coalescing** - Coalesced bursty inbox auto-refreshes and gated `JobsApp` cell updates on data changes, stopping excessive refreshes over Cloudflare tunnel connections.
- **Sequential Agent Read Optimization** - Reduced process spin-up overhead during sequential file reads by coding agents.
- **Dashboard Cost Chart Currency** - Added currency indicators ($) to the cost chart bar series on the dashboard.
- **Jobs Table Formatting** - Flattened markdown links and formatting in the Jobs table prompt/title column for cleaner tabular output.
- **Piped CLI Output and JSON Support** - Added ASCII fallbacks for piped CLI table rendering and introduced `--json` output flag for `tendril verification list`.
- **Legacy .NET Tool Redirection** - Added doctor checks and automatic redirection to route legacy `.NET` tool invocations (`ivy-tendril`) to the installed Tendril CLI.
- **Documentation Redesign** - Redesigned `README.md` following the Orca layout with updated feature GIFs.

### Bug Fixes

- **Project Name Validation** - Added strict project name validation across the CLI, Settings dialog, and onboarding flow to reject invalid names and prevent crashes during setup.
- **CLI Boot Overhead** - Prevented the Tendril server from booting up when `--help`, `-h`, or unrecognized arguments are passed to `tendril`.
- **Job Status 404 Reporting** - Made `tendril job status` and `tendril job fail` endpoints best-effort instead of throwing fatal 404 errors.
- **Duplicate Analyzer Warning** - Removed duplicate `Ivy.Analyser` PackageReference in `Ivy.Tendril.csproj` eliminating NU1504 warnings, and updated `UpdateIvyPackages.ps1` to edit versions in-place.
- **PlatformHelper Shell Execution** - Explicitly set `UseShellExecute` to `false` for `open` (macOS) and `xdg-open` (Linux) commands in `PlatformHelper`.

## 1.1.16 (2026-07-24)

### Features

- **Ivy Agent Integration** - Introduced integration for the standalone Ivy Agent, including a one-click CDN installer in Settings, custom Ivy Proxy URL settings, and beta flag gating (`TENDRIL_BETA` or `IVY_BETA`).
- **Compact Badge Selectors** - Replaced full-width project and priority select inputs in the Create Plan dialog with scrollable compact badge buttons (`BadgeSelect` widget).

### Improvements

- **Cloudflare Tunnel DNS Diagnostics** - Surfaced detailed startup and connection diagnostic messages for `cloudflared` tunnel failures.
- **Settings View Cleanup** - Refactored settings inputs to use native C# `.Description(...)` builder properties for visual consistency.

### Bug Fixes

- **Add Project Dialog Layering** - Made the "New Project" button in Create Plan open the Add Project dialog directly on top without navigating away.
- **Tunnel URL Parsing** - Fixed cloudflared tunnel URL extraction by ignoring internal `api.trycloudflare.com` domain references.

## 1.1.14 (2026-07-21)

### Features

- **Third-Party Notices Documentation** - Added `THIRD_PARTY_NOTICES.md` to document bundled third-party dependency licenses.

### Improvements

- **ContentInput Optimistic State** - Implemented optimistic local text state updates in the `ContentInput` widget, deferring background property updates while typing to prevent input clobbering.

### Bug Fixes

- **Cloudflared Downloader** - Resolved a setup crash in the automatic `cloudflared` binary installer and downloader.
- **Codex Failure Analysis** - Fixed Codex coding agent failure parsing and model catalog validation.
- **Tunnel Settings Layout** - Corrected string and layout typos in the Tunnel Setup Settings screen.

## 1.1.13 (2026-07-20)

### Features

- **Batch Recommendations Implementation** - Batch-select and implement multiple recommendations at once in the Review app.
- **Investigate and Discuss with Agent** - Added "Investigate with Agent" and "Discuss with Agent" action buttons in Drafts, Review, and the Job Debug sheet.
- **Plan Add Worktree CLI Command** - Added `tendril plan add-worktree` CLI command for symmetric worktree management.
- **Rerun Completed RetryPlan Jobs** - Added ability to rerun completed `RetryPlan` jobs directly from the Jobs list.

### Improvements

- **Config Auto-Reload** - Automatically reload configuration on external file edits.
- **Review Summary and Tabs** - Rendered Review Summary as DraftMarkdown with sticky Verifications card and extracted Review tabs into dedicated views.
- **Job Log Consolidation** - Consolidated all job execution logs under a unified `<TendrilHome>/Jobs/` directory.
- **Sidebar and Table Row Highlighting** - Enhanced sidebar list items and data tables with filled background selection styling.
- **Desktop Framework Update** - Updated Ivy framework dependencies to 1.3.8 and configured desktop About dialog details.

### Bug Fixes

- **State Preservation on Job Deletion** - Preserved completed plan state when deleting finished jobs.
- **Markdown and Math Rendering** - Fixed prose dollar signs rendering as LaTeX math and fixed inline code styling in DraftMarkdown.
- **Job Slot Semaphore Leak** - Fixed a semaphore leak in job slot allocation during unhandled launch failures.
- **Jobs Starting State Hang** - Fixed jobs hanging indefinitely in starting state by adding launch error handling.
- **Worktree Commit Sync** - Ensured commits from all plan worktrees are synchronized correctly.

## 1.1.12 (2026-07-03)

### Improvements

- **Dotnet Verification Solvers** — Updated the `DotnetBuild`, `DotnetFormat`, `DotnetTest`, and `FrameworkDotnetBuild` verification prompts to locate the solution file explicitly. Added scoping notes for multi-repository configurations, ensuring reliable builds and tests.
- **Pull Request UI Layout** — Reordered the PullRequest app columns to show Plan first and Repository last, and narrowed the Cost and Tokens columns to 80px for a more compact and readable table layout.

### Bug Fixes

- **Concurrent CreatePr Body Swap** — Fixed a race condition where concurrent `CreatePr` jobs could swap or overwrite each other's pull request descriptions due to non-unique body text files. Switched to `mktemp` for unique body-file creation and added regression tests.
- **Shared Event Parser Race** — Resolved an event-parsing race condition by isolating parsers per session rather than sharing parser instances. Added regression tests to prevent future multi-session race conditions.

## 1.1.11 (2026-07-03)

### Bug Fixes

- **macOS Installer & Startup Fix** — Fixed a critical issue where the macOS installer (.pkg) completed successfully but failed to install or launch the application due to broken symlinks and codesign signatures during repackaging. Replaced `pkgutil --expand-full` with `pkgutil --expand` to preserve app payload integrity, corrected the target directory to `1.pkg/Scripts/postinstall`, and fixed a path typo in the localhost certificate trusting script.

## 1.1.10 (2026-07-03)

### Bug Fixes

- **macOS Installer Notarization** — Fixed macOS installer notarization by properly submitting and stapling the repacked installer package.

## 1.1.9 (2026-07-03)

### Features

- **Promptware File Input** — Added support for file-based content input to promptware write commands, allowing promptwares to ingest local files during execution.
- **Plan Revision Recovery CLI** — Added a new `plan get-revision` CLI command to retrieve and inspect historical revisions of a plan.
- **SyncRepo Untracked-Changes Policy** — Added configurable untracked-changes policy options (Stash/Commit/PullRequest) for SyncRepo execution.
- **Antigravity CLI Graduation** — Graduated the Antigravity CLI integrations and checks to fully stable status.

### Improvements

- **Universal Bug Reporting** — Enabled bug reporting under all agents by normalizing target models to backend-supported families and appending original agent metadata, and fixed bug reporting on macOS by recursively collecting plan files and ignoring worktree folders early.
- **Verification CLI Fallbacks** — The `verification` commands now automatically list all available verification scripts if the specified verification name is not found.
- **DraftMarkdown Widget Styles** — Synchronized the styling of the DraftMarkdown widget with the latest core design system updates.

## 1.1.8 (2026-07-03)

### Features

- **Desktop Self-Update Capability** — Implemented self-update capability and dialog, allowing the desktop application to check and update itself to the latest version automatically.
- **Tools Folder Persistence** — Preserves the `Tools/` directory during promptware upgrades and guarantees that promptware runtime folders are correctly structured.

### Improvements

- **Drafts App Shortcut** — Added the `Backspace` keyboard shortcut to trigger the Delete action in the Drafts app (resolving #1507).
- **Responsive Layout Spacing** — Realigned the issue link button in the responsive header to prevent overlapping and text wrapping.

## 1.1.7 (2026-07-02)

### Features

- **Localhost HTTPS Certificate Generation** — Automatically generate and package secure localhost SSL/TLS certificates for macOS and Windows desktop applications, enabling local HTTPS out-of-the-box.
- **Create Plan Dialog Enhancement** — Added a direct "New Project" shortcut link to the Create Plan dialog for quicker onboarding.
- **Claude Fable 5 Selection** — Added `Claude Fable 5` as a selectable model choice in model configurations.
- **Config CLI & MCP Integration** — Added first-class `config get` and `config set` commands to the Tendril CLI and Model Context Protocol (MCP) server endpoints.
- **FieldToolsDemo Experiment** — Introduced a new `FieldToolsDemo` experiment for developer testing.

### Improvements

- **Optimistic Job Deletion** — Made job deletion optimistic by delegating git worktree cleanup tasks to background threads, yielding faster UI response.

### Bug Fixes

- **CI Workflows & Scripts** — Fixed a YAML syntax error in the publication workflow, resolved SSL certificate generation crashes in CI pipelines, and corrected a syntax error in the macOS post-installation script.

## 1.1.6 (2026-07-02)

### Features

- **First-class failure reporting** — Added the `tendril job fail <job-id> --message` CLI command allowing promptwares to report specific execution failures explicitly instead of relying on exit codes and raw stdout heuristics.
- **Inbox auto-refresh** — Replaced interval-based polling in Drafts, Review, Icebox, Recommendations, and Trash apps with subscription-based updates using a debounced process status and file system watcher.
- **Velopack updater consolidation** — Consolidated the desktop self-update flow onto Velopack, enabling check-for-updates in Settings, persisting dismissed updates across restarts, and removed the obsolete `Ivy.Tendril.Updater` project.
- **UserQuestion widget** — Added a new `UserQuestion` widget and viewer for interactive user prompts.
- **Onboarding guide** — Added a first-class onboarding guide to the Getting Started documentation.
- **New plan enhancements** — Added a project select button directly to the Create Plan dialog, and renamed `CustomPrDialog` to `CreatePrDialog`.

### Improvements

- **Windows path and shell safety** — Replaced shell-unsafe characters (pipes and parentheses) in the `stackHash` project configuration with `/` and `.ts` extensions, and implemented Windows CLI argument escaping.
- **Agent sandbox network access** — Enabled sandboxed network access for Codex via the `sandbox_workspace_write.network_access` setting, fixing PermissionError on socket bind operations.
- **OpenCode local Ollama support** — Bypassed auth checks and resolved the binary path automatically when running OpenCode with a local Ollama model, and switched to `--auto` execution to prevent PTY hangs.
- **Markdown link handling** — Centralized plan-revision markdown link polishing and rendering safety checks to strip line-number anchors from file URLs.
- **Bug report GitHub username** — Added an optional GitHub username field to the Bug Report dialog and `report-bug` CLI command.
- **UI layout refinements** — Hidden the Tunnel QR panel on mobile/tablet screens, nested the loading spinner within the starting callout, fixed the "Stop" button icon, and restored spacing in the Review actions layout.
- **Text unwrapping for Gemini** — Added text unwrapping for Gemini's hard line break formatting to improve readability.
- **Keyboard element styling** — Added styling for `<kbd>` elements in the markdown widget.

### Bug Fixes

- Fixed jobs hanging indefinitely in the pre-launch window due to deadlocks or stale output by arming timeouts immediately and executing before-hooks concurrently.
- Fixed the Create Plan screen scroll position resetting/twitching to the top when navigating tabs.
- Fixed job cost calculation for timed-out runs by falling back to pricing-based calculations when inline cost is zero or missing.
- Fixed CreatePr plans remaining in Drafts when agents skip closeout steps by automatically parsing PR URLs from output on completion.
- Fixed startup session log spam and em-dash formatting in master election logs.
- Fixed EPERM listen errors on startup by binding test servers to loopback.
- Fixed Codex agent output collapsing to zero height during execution.
- Fixed keyboard focus/blur issues and auto-focused the input when the New Plan dialog opens.
- Disabled the unused Tunnel feature in default configuration.

## 1.1.1 (2026-06-25)

### Features

- **Voice & rich plan input** — New ContentInput widget brings voice transcription and file attachments to the Create Plan dialog; files upload over HTTP POST and are stored alongside the plan, with drag-and-drop support.
- **Chat with Agent** — Beta AgentApp lets you chat directly with the coding agent over a PTY, with a "Chat with Agent" button in the New Plan dialog and the `tendril` CLI exposed to the agent via a shim.
- **Plan annotations** — Annotate drafts in DraftsApp to drive annotation-based plan updates.
- **Mobile & tablet support** — Tendril is now responsive across mobile, tablet, and desktop breakpoints, with adaptive headers, sheets, pickers, and process viewer.
- **DraftMarkdown widget** — Renders Mermaid and Graphviz diagrams, callouts, local-file and clickable images, and inline text annotations.
- **Velopack auto-updates** — Desktop app self-updates via Velopack, with installer name-collision prevention.
- **Activity heatmap** — Wallpaper app shows a 90-day completed-PR activity heatmap.
- **SyncRepo & preflight dirty-repo check** — New SyncRepo promptware plus a preflight check that detects and resolves dirty repository state before Execute and Create Plan.
- **Job dependencies** — Job-level `WaitForJobs` blocking with cascade failure, periodic re-evaluation of blocked jobs, and a Force Start action for blocked jobs.
- **Rerun with feedback** — Rerun a job with additional feedback for the agent.
- **Revert revision** — Revert a specific plan revision directly from the Details tab.
- **Stale-worktree reaper** — Bounds worktree disk usage by reaping stale worktrees left from prior runs.
- **HTTP-based CLI/server IPC** — CLI and server communicate over HTTP with master election for reliable single-instance coordination.
- **Bundled runtimes** — .NET 10 SDK and PowerShell 7 are bundled in installers and resolved dynamically at runtime when present.
- **Repo guardrails** — Plans are guarded against executing or merging in repositories outside their project, and the repo's default branch is detected instead of assuming `main`.
- **Plan migration framework** — Added `schemaVersion` to `plan.yaml` with a per-file plan migration framework.
- **Coding agent environment variables** — Configure per-agent environment variables in Coding Agent settings.
- **`tendril agent-instructions` command** — Output the agent instructions from the CLI.

### Improvements

- **Tunnel polish** — Connecting state, wallpaper QR code, Open in Browser, routable-before-Connected detection, orphaned `cloudflared` cleanup, and single-click deactivate with optimistic UI.
- **Verifications as single source of truth** — `plan.yaml` is now the source of truth for verifications, with a dedicated UI card, status enum, and drag-and-drop ordering in the project edit dialog.
- **Job Debug sheet** — Added working directory and CLI arguments, copy buttons for Plan/Job IDs, a Report Bug button, and promptware learnings (memory/tool writes); hides empty rows and permission denials.
- **Plan state renames** — `Building → Creating` and `ReadyForReview → Review` for clearer lifecycle naming.
- **CLI consolidation** — Single-channel logging, unified exception propagation, descriptive job-status output, and added Web API/MCP endpoints for full CLI parity.
- **Recommendations simplified** — Removed the Risk field from recommendations across the UI and prompts.
- **macOS standalone app** — Robust login-shell PATH and environment loading, correct packaged-app detection, and automatic global `tendril` symlink creation.
- **Widget restructure** — Consolidated widgets into a unified `Ivy.Tendril.Widgets` project with per-widget frontend directories.
- **Auto-merge workflow** — CI workflow automatically merges `main` back into `development` after release.
- **Dependency security** — Upgraded `SQLitePCLRaw.lib.e_sqlite3` to 3.50.3 and pinned frontend dependencies (dompurify, vite-plus) to address known vulnerabilities.

### Bug Fixes

- Fixed `tendril plan create` dash-value argument parsing.
- Fixed SQLite "database is locked" errors via a shared connection factory and `busy_timeout`.
- Fixed cancelled/stopped/failed jobs reverting plans to their previous state.
- Fixed PR merge depending on a stale `prRule` instead of the `PrMerge` flag.
- Fixed drafts not refreshing after changes.
- Fixed intermittent Create PR failures and misleading error messages.
- Fixed Review and Drafts markdown left padding not rendering.
- Fixed verification order not persisting in the Edit Project dialog.
- Fixed job cost calculation to run for all statuses using inline result data.
- Fixed `plan.yaml` lost-write race condition when accepting a recommendation.
- Fixed crash when navigating to Drafts/Review with an invalid plan.
- Fixed race condition in `WaitForJobs` unblocking and duplicate job detection.
- Fixed IvyFrameworkVerification leaving zombie processes after test runs.
- Fixed Copilot usage-metric parsing crashes with defensive parsing.
- Fixed Spectre.Console crash from unescaped markup in doctor output.
- Fixed onboarding startup crash on macOS and Windows when `TENDRIL_HOME` is empty.
- Fixed duplicate Default option in coding agent profile model dropdowns.
- Fixed missing Windows taskbar icon.
- Fixed plan folder ACL permissions blocking ExecutePlan.
- Fixed duplicate SyncRepo jobs being queued for the same repository.
- Fixed ContentInput name collision after the framework added its own widget.
- Fixed JS `SyntaxError` on older WebKit by targeting es2020.

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

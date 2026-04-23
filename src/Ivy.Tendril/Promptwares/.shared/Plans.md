# Plans File Structure

Plans live under `planFolder` from `config.yaml`.

## Directory Layout

```
{planFolder}/
├── .counter                          # Next plan ID (integer, auto-incremented)
├── 01098-MakeAnEmptyAppCalledReview/
│   ├── plan.yaml                     # Plan metadata
│   ├── revisions/                    # Plan content versions
│   │   ├── 001.md                    # Initial revision (created by CreatePlan)
│   │   ├── 002.md                    # After ExpandPlan/UpdatePlan/SplitPlan
│   │   └── ...
│   ├── logs/                         # Execution logs per promptware run
│   │   ├── 001-CreatePlan.md
│   │   ├── 002-ExpandPlan.md
│   │   └── ...
│   ├── artifacts/                    # Output artifacts from execution
│   │   ├── tests/                    # Test scripts and data
│   │   ├── screenshots/              # UI screenshots
│   │   └── sample/                   # Sample apps exercising new functionality
│   ├── verification/                 # Verification reports
│   │   ├── DotnetBuild.md
│   │   ├── DotnetTest.md
│   │   └── ...
│   ├── worktrees/                    # Git worktrees used during execution
│   ├── temp/                         # Scratch space for promptware agents
└── ...
```

## Folder Naming

`{ID:D5}-{SafeTitle}` — e.g. `01098-MakeAnEmptyAppCalledReview`

- **ID**: 5-digit value from `.counter`
- **SafeTitle**: Title-cased, first 60 chars of description, alphanumeric only, no spaces (e.g. `"Fix login bug"` – `FixLoginBug`)

## Modifying Plans — Use the CLI

**IMPORTANT: Never read or write `plan.yaml` directly.** Always use `tendril plan` CLI commands. This ensures validation, atomic writes, timestamp updates, and database sync.

Plan IDs can be provided in any of these forms:
- Full path: `D:\Plans\00015-LogWarning`
- Folder name: `00015-LogWarning`
- Zero-padded ID: `00015`
- Bare number: `15`

### Reading plan data

```bash
# Full YAML
tendril plan get <plan-id>

# Individual scalar fields
tendril plan get <plan-id> state
tendril plan get <plan-id> project
tendril plan get <plan-id> title
tendril plan get <plan-id> level
tendril plan get <plan-id> priority
tendril plan get <plan-id> created
tendril plan get <plan-id> updated
tendril plan get <plan-id> executionProfile
tendril plan get <plan-id> initialPrompt
tendril plan get <plan-id> sourceUrl

# List fields (one item per line)
tendril plan get <plan-id> repos
tendril plan get <plan-id> prs
tendril plan get <plan-id> commits
tendril plan get <plan-id> verifications      # Format: Name=Status
tendril plan get <plan-id> dependsOn
tendril plan get <plan-id> relatedPlans
tendril plan get <plan-id> recommendations    # Format: Title=State
```

### Writing plan data

```bash
# Set scalar fields
tendril plan set <plan-id> state <value>
tendril plan set <plan-id> project <value>
tendril plan set <plan-id> title <value>
tendril plan set <plan-id> level <value>
tendril plan set <plan-id> priority <value>
tendril plan set <plan-id> executionProfile <value>

# Manage repos
tendril plan add-repo <plan-id> <repo-path>
tendril plan remove-repo <plan-id> <repo-path>

# Track PRs and commits
tendril plan add-pr <plan-id> <pr-url>
tendril plan add-commit <plan-id> <sha>

# Verifications
tendril plan set-verification <plan-id> <name> <status>
# Valid statuses: Pending, Pass, Fail, Skipped

# Related plans
tendril plan add-related-plan <plan-id> <folder-name>

# Dependencies
tendril plan add-depends-on <plan-id> <folder-name>

# Recommendations
tendril plan rec add <plan-id> <title> -d <description> [--impact Small|Medium|High] [--risk Small|Medium|High]
tendril plan rec accept <plan-id> <title> [--notes <text>]
tendril plan rec decline <plan-id> <title> [--reason <text>]
tendril plan rec set <plan-id> <title> <field> <value>
tendril plan rec remove <plan-id> <title>
tendril plan rec list <plan-id> [--state Pending|Accepted|Declined]

# Replace entire plan YAML (pipe from stdin)
cat revised.yaml | tendril plan update <plan-id>

# Validate plan health
tendril plan validate <plan-id>
```

### Creating a plan

```bash
tendril plan create <plan-id> <title> [options]
```

Options:
- `--project <name>` — Project name (default: Auto)
- `--level <level>` — Priority level (default: NiceToHave)
- `--initial-prompt <text>` — Original user description
- `--source-url <url>` — GitHub issue or PR URL
- `--execution-profile <profile>` — deep or balanced
- `--priority <number>` — Priority (default: 0)
- `--repo <path>` — Repository path (repeatable)
- `--verification <Name=Status>` — Verification entry (repeatable)
- `--related-plan <folder>` — Related plan folder name (repeatable)
- `--depends-on <folder>` — Dependency plan folder name (repeatable)

### Writing execution logs

```bash
tendril plan add-log <plan-id> <action> [--summary <text>]
```

### Cleaning up worktrees

```bash
tendril plan cleanup <plan-id> [--force]
```

### Checking plan health

```bash
tendril plan doctor [--all] [--fix] [--prune] [--state <state>] [--worktrees]
```

### Running a promptware

Run a promptware directly (synchronous, blocks until completion):

```bash
tendril promptware <name> [<plan-folder>] [--profile <profile>] [--working-dir <dir>] [--value key=value]
```

Example — run a verification promptware from within ExecutePlan:

```bash
tendril promptware IvyFrameworkVerification D:\Plans\01234-MyPlan --value VerificationDir=D:\Plans\01234-MyPlan\verification --value ArtifactsDir=D:\Plans\01234-MyPlan\artifacts
```

## plan.yaml

```yaml
state: Draft
project: Tendril
level: NiceToHave
title: "Make an empty app called Review"
sessionId: "a1b2c3d4-e5f6-..."
repos: []
created: 2026-03-28T20:36:39Z
updated: 2026-03-28T20:36:39Z
initialPrompt: "Make an empty app called Review"
sourceUrl: "https://github.com/owner/repo/issues/42"
prs: []
commits: []
verifications:
  - name: DotnetBuild
    status: Pending
  - name: DotnetTest
    status: Pending
relatedPlans: []
dependsOn: []
priority: 0
recommendations:
  - title: Add error handling
    description: The service lacks retry logic
    state: Pending
    impact: Medium
    risk: Small
```

### Fields

| Field          | Description                                      |
|----------------|--------------------------------------------------|
| `state`        | Current plan state (see lifecycle below)         |
| `project`      | Project name matching a `projects` entry in `config.yaml` |
| `level`        | One of the levels defined in `config.yaml`       |
| `title`        | Human-readable plan title                        |
| `sessionId`    | Claude session ID from CreatePlan (for `claude --resume`) |
| `repos`        | Affected repository paths (plain strings, e.g. `- D:\Repos\Foo` on Windows or `- /home/user/repos/Foo` on Linux — NOT objects) |
| `created`      | UTC timestamp when the plan was created (use `CurrentTime` from firmware header) |
| `updated`      | UTC timestamp of last state change (use `CurrentTime` from firmware header)      |
| `initialPrompt`| Original user description                        |
| `prs`          | Associated pull request URLs                     |
| `commits`      | Associated commit hashes                         |
| `verifications`| List of `{name, status}` — status is `Pending`, `Pass`, `Fail`, or `Skipped` |
| `sourceUrl`    | (Optional) GitHub PR or issue URL that triggered this plan |
| `sourcePath`   | (Optional) Absolute path to the source that generated this plan (e.g. test working directory) |
| `relatedPlans` | Paths to related plan folders (parent plans, split-from, follow-ups) |
| `dependsOn`    | Plan folder names this plan depends on (e.g. `- 01478-WorktreeIsolation`). ExecutePlan will block until all dependencies are `Completed` and their PRs are merged. |
| `priority`     | Integer priority (0 = normal). Higher values are executed first. Set by CreatePlan launcher, not by agents. |
| `executionProfile` | (Optional) Recommended execution profile for ExecutePlan: `deep` or `balanced`. If set, overrides config.yaml default. CreatePlan sets this based on task complexity analysis. |
| `recommendations` | (Optional) List of recommendations discovered during ExecutePlan. Each entry has `title`, `description`, `state` (Pending/Accepted/AcceptedWithNotes/Declined), `declineReason`, `impact` (Small/Medium/High), and `risk` (Small/Medium/High). |

**Do NOT add fields beyond those listed above.** Unknown fields (e.g. `tags`, `category`) will be stripped by the normalizer and may cause parse errors.

## State Lifecycle

```
CreatePlan ──► Draft
               │
               ├─ ExpandPlan ──► Building ──► Draft
               ├─ UpdatePlan ──► Updating ──► Draft
               ├─ SplitPlan  ──► Updating ──► Skipped
               │
               ├─ ExecutePlan (dependencies unmet)
               │    Draft ──► Blocked ──► Draft (when unblocked) ──► Building ──► ...
               │
               ├─ ExecutePlan (Execute button)
               │    Draft ──► Building ──► Executing ──► ReadyForReview
               │                                    └──► Failed
               │
               ├─ CreatePr (from Review app)
               │    ReadyForReview ──► Completed
               │
               ├─ (manual) ──► Skipped
               └─ (manual) ──► Icebox
```

| State            | Meaning                                    | Visible in      |
|------------------|--------------------------------------------|-----------------|
| `Draft`          | Ready for review/action                    | Plans           |
| `Building`       | ExpandPlan or ExecutePlan in progress       | Jobs            |
| `Updating`       | UpdatePlan or SplitPlan in progress         | Jobs            |
| `Executing`      | ExecutePlan agent running                   | Jobs            |
| `ReadyForReview` | ExecutePlan finished, awaiting human review | Review          |
| `Failed`         | ExecutePlan errored                         | Review          |
| `Completed`      | PR created, plan done                       | —               |
| `Skipped`        | Manually dismissed or split                 | —               |
| `Blocked`        | Waiting for dependency plans to complete     | Plans           |
| `Icebox`         | Parked for later                            | Icebox          |

## Revisions

Markdown files in `revisions/` numbered sequentially (`001.md`, `002.md`, ...).

The initial revision is created by CreatePlan using the `planTemplate` from `config.yaml`.

Subsequent revisions are written by ExpandPlan, UpdatePlan, or SplitPlan agents.

## Logs

`logs/{NNN}-{Action}.md` per promptware run (Completed time, status, …).

## temp/

Scratch for clones, downloads, intermediates. Safe to delete after the plan finishes.

## .counter

Single integer in `{planFolder}/.counter`; CreatePlan reads and increments for new IDs.

## Verifications

Each revision can include `## Verification` with checkboxes from `config.yaml`:

```markdown
## Verification

- [x] DotnetBuild
- [x] DotnetTest
- [ ] FrontendLint
```

`- [x]` = ExecutePlan will run; `- [ ]` = skipped. Definitions live in top-level `config.yaml` `verifications`; projects reference by name + `required`.

## Notes

- **Local file links in plans:** Use format `[FileName.ext:line](file:///full/path/to/FileName.ext)` for clickable links that VS Code can open. Link text should be just the filename (with optional `:line` or `:start-end` line numbers). Link URL should be the full `file:///` path without line number fragments (`#L123`). Examples:
  - Single line: `[JobsApp.cs:205](file:///D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Apps\JobsApp.cs)`
  - Line range: `[Utils.cs:42-50](file:///D:\Repos\_Ivy\Ivy-Framework\src\Ivy\Utils.cs)`
  - File without line: `[Program.cs](file:///D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Program.cs)`
  - Never use backticks in link text or `#L` fragments in URLs
- **Plan references:** `[Plan 03156](plan://03156)` to link to other plans. The link handler will navigate to that plan in the Plans app. The plan ID can be 5 digits (e.g., `plan://03156`) or without leading zeros (e.g., `plan://3156`).
- Images: normal markdown `![alt](url)`.
- **Diagrams:** Graphviz/DOT (```dot / ```graphviz) or Mermaid (```mermaid). **Prefer DOT** for layout. Use only when a diagram really helps.

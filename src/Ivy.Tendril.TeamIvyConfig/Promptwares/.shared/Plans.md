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
| `verifications`| List of `{name, status}` — status is `Pending`, `Pass`, or `Fail` |
| `sourceUrl`    | (Optional) GitHub PR or issue URL that triggered this plan |
| `sourcePath`   | (Optional) Absolute path to the source that generated this plan (e.g. test working directory) |
| `relatedPlans` | Paths to related plan folders (parent plans, split-from, follow-ups) |
| `dependsOn`    | Plan folder names this plan depends on (e.g. `- 01478-WorktreeIsolation`). ExecutePlan will block until all dependencies are `Completed` and their PRs are merged. |
| `priority`     | Integer priority (0 = normal). Higher values are executed first. Set by CreatePlan launcher, not by agents. |
| `executionProfile` | (Optional) Recommended execution profile for ExecutePlan: `deep`, `balanced`, or `quick`. If set, overrides config.yaml default. CreatePlan sets this based on task complexity analysis. |

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
- [ ] FrameworkFrontendLint
```

`- [x]` = ExecutePlan will run; `- [ ]` = skipped. Definitions live in top-level `config.yaml` `verifications`; projects reference by name + `required`.

## Notes

- **Local file links in plans:** `[Button.cs](file:///path/to/...)` so VS Code opens the path; keep the path as link text.
- **Plan references:** `[Plan 03156](plan://03156)` to link to other plans. The link handler will navigate to that plan in the Plans app. The plan ID can be 5 digits (e.g., `plan://03156`) or without leading zeros (e.g., `plan://3156`).
- Images: normal markdown `![alt](url)`.
- **Diagrams:** Graphviz/DOT (```dot / ```graphviz) or Mermaid (```mermaid). **Prefer DOT** for layout. Use only when a diagram really helps.

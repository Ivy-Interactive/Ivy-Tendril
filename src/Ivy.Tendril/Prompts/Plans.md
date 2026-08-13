# Plans File Structure

Plans live under `planFolder` from `config.yaml`.

## Directory Layout

```
{planFolder}/
├── .counter                          # Next plan ID (integer, auto-incremented)
├── 01098-MakeAnEmptyAppCalledReview/
│   ├── plan.yaml                     # Plan metadata
│   ├── Revisions/                    # Plan content versions
│   │   ├── 001.md                    # Initial revision (created by CreatePlan)
│   │   ├── 002.md                    # After ExpandPlan/UpdatePlan/SplitPlan
│   │   └── ...
│   ├── Logs/                         # Execution logs per promptware run
│   │   ├── 001-CreatePlan.md
│   │   ├── 002-ExpandPlan.md
│   │   └── ...
│   ├── Artifacts/                    # Output artifacts from execution
│   │   ├── tests/                    # Test scripts and data
│   │   ├── screenshots/              # UI screenshots
│   │   └── sample/                   # Sample apps exercising new functionality
│   ├── Verification/                 # Verification reports
│   │   ├── DotnetBuild.md
│   │   ├── DotnetTest.md
│   │   └── ...
│   ├── Worktrees/                    # Git worktrees used during execution
└── ...
```

## Folder Naming

`{ID:D5}-{SafeTitle}` — e.g. `01098-MakeAnEmptyAppCalledReview`

- **ID**: 5-digit value from `.counter`
- **SafeTitle**: Title-cased, first 60 chars of description, alphanumeric only, no spaces (e.g. `"Fix login bug"` – `FixLoginBug`)

**SafeTitle is for the folder name only.** It is derived automatically from the title by the CLI — do not pass it anywhere. The plan's `title` field is a separate, **human-readable** string (Title Case *with spaces*). Never reuse the PascalCase SafeTitle form as the `title`.

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
tendril plan get <plan-id> partialDelivery    # true: Completed over a failed verification, so the
                                              # deliverable may be missing

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
# `state Completed` is refused while any verification is Fail. Re-run the verification, or set it to
# Skipped with a reason. Add --allow-failed-verifications only to record a deliberate partial
# delivery; it also sets partialDelivery: true.
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
tendril plan rec add <plan-id> <title> -d <description> [--impact=Small|Medium|High]
tendril plan rec accept <plan-id> <title> [--notes=<text>]
tendril plan rec decline <plan-id> <title> [--reason=<text>]
tendril plan rec set <plan-id> <title> <field> <value>
tendril plan rec remove <plan-id> <title>
tendril plan rec list <plan-id> [--state=Pending|Accepted|Declined]

# Validate plan health
tendril plan validate <plan-id>
```

### Creating a plan

```bash
tendril plan create <title> <project> [options]
```

Auto-allocates a plan ID, creates the folder, and writes `plan.yaml`. Repos are derived from the project configuration.

Pass options in the `--option=value` (equals) form, e.g. `--initial-prompt="..."`. The parser reads any token starting with `-` as an option name, so the space-separated form breaks when a value itself begins with a dash (e.g. a prompt opening with a `-` bullet). Likewise, `<title>` must not begin with a `-`.

Outputs:
```
PlanId: <ID>
Directory: <TendrilPlansFolder>/<ID>-<SafeTitle>
Verifications:
<Name>:<Status>
<Name>:<Status>
...
```

Options:
- `--level <level>` — Priority level (default: Feature)
- `--initial-prompt <text>` — Original user description
- `--source-url <url>` — GitHub issue or PR URL
- `--execution-profile <profile>` — deep or balanced
- `--priority <number>` — Priority (default: 0)
- `--verification <Name=Status>` — Verification entry (repeatable)
- `--related-plan <folder>` — Related plan folder name (repeatable)
- `--depends-on <folder>` — Dependency plan folder name (repeatable)

### Writing revisions

```bash
tendril plan write-revision <plan-id> --stdin <<'EOF'
<revision content>
EOF
```

Reads content from STDIN (when `--stdin` is passed) or `--file`, and writes it to `revisions/<NNN>.md` in the plan folder. Auto-increments from the highest existing revision. Outputs the file path.

### Writing execution logs

```bash
tendril job add-log <job-id> <action> [--summary=<text>]
```

Appends an `## Agent Log` section to your own job's log in `<TendrilHome>/Jobs/`. Pass the
`TendrilJobId` value from your firmware header as `<job-id>`.

### Cleaning up worktrees

```bash
tendril plan cleanup <plan-id> [--force]
```

## plan.yaml

```yaml
state: Draft
project: Tendril
level: Feature
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
| `title`        | Human-readable plan title in **Title Case with spaces** (e.g. `Show File Details in Local Changes Dialog`). **Never** PascalCase / no-space form (`ShowFileDetailsInLocalChangesDialog`) — that form is reserved for the folder `SafeTitle` only. MUST be identical to the `# {title}` H1 heading in the revision markdown. |
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
| `executionProfile` | (Optional) Recommended execution profile for ExecutePlan: `deep` or `balanced`. If set, overrides config.yaml default. CreatePlan sets this based on task complexity analysis. |

**Do NOT add fields beyond those listed above.** Unknown fields (e.g. `tags`, `category`) will be stripped by the normalizer and may cause parse errors.

## State Lifecycle

```
CreatePlan ──► Draft
               │
               ├─ ExpandPlan ──► Creating ──► Draft
               ├─ UpdatePlan ──► Updating ──► Draft
               ├─ SplitPlan  ──► Updating ──► Skipped
               │
               ├─ ExecutePlan (dependencies unmet)
               │    Draft ──► Blocked ──► Draft (when unblocked) ──► Creating ──► ...
               │
               ├─ ExecutePlan (Execute button)
               │    Draft ──► Creating ──► Executing ──► Review
               │                                    └──► Failed
               │
               ├─ CreatePr (from Review app)
               │    Review ──► Completed
               │
               ├─ (manual) ──► Skipped
               └─ (manual) ──► Icebox
```

| State            | Meaning                                    | Visible in      |
|------------------|--------------------------------------------|-----------------|
| `Draft`          | Ready for review/action                    | Plans           |
| `Creating`       | ExpandPlan or ExecutePlan in progress       | Jobs            |
| `Updating`       | UpdatePlan or SplitPlan in progress         | Jobs            |
| `Executing`      | ExecutePlan agent running                   | Jobs            |
| `Review` | ExecutePlan finished, awaiting human review | Review          |
| `Failed`         | ExecutePlan errored                         | Review          |
| `Completed`      | PR created, plan done                       | —               |
| `Skipped`        | Manually dismissed or split                 | —               |
| `Blocked`        | Waiting for dependency plans to complete     | Plans           |
| `Icebox`         | Parked for later                            | Icebox          |

## Revisions

Markdown files in `revisions/` numbered sequentially (`001.md`, `002.md`, ...).

The initial revision is created by CreatePlan using the `planTemplate` from `config.yaml`.

Subsequent revisions are written by ExpandPlan, UpdatePlan, or SplitPlan agents.

## Question Blocks

Promptwares run headless and cannot ask the user anything mid-run. A planning agent that hits a
genuine ambiguity instead emits one or more fenced `questions` blocks in the revision markdown. The
user answers them in the UI, which writes the answers back into the same blocks; UpdatePlan then
folds those answers into the plan and retires the questions.

Emit a block only for an ambiguity that research cannot settle and that changes what gets built. A
question you can answer by reading the code is not a question, it is research you skipped.

A block holds 1-4 questions. There is no cap on how many blocks a revision may contain. Place each
block wherever it makes the most sense — right after the `# {title}` H1 for a question about overall
scope, or inline under the `## Solution` subsection it concerns for a question about one design
decision.

````
```questions
questions:                    # 1-4 items
  - title:       string       # required, the question
    header:      string       # optional, <=12 char chip label; derived from title if absent
    description: markdown     # optional, context shown under the question
    multiple:    bool         # optional, default false; true = multi-select
    other:       bool         # optional, default true; user may type a free value
    options:                  # 2-4 items; omit entirely for a pure free-text question
      - title:       string   # required, 1-5 words
        description: markdown # optional, the expanded body for this option
        value:       slug     # required, stable id used by `answer`; ^[a-z0-9][a-z0-9-]*$
        recommended: bool     # optional, max one per question
    answer:      value | [values] | string   # filled in on response
```
````

Three shapes fall out of `multiple` / `other` / the presence of `options`:

| Shape | How |
|---|---|
| Single-select, fixed set | `other: false` plus options |
| Multi-select, open set | `multiple: true` plus options |
| Pure free text | no options at all |

### Answer semantics

- An entry that matches an option's `value` is that option.
- An entry that matches nothing is the user's own text. Legal when `other` is true, or when there are
  no options.
- `multiple: true` means `answer` is always a list, even with one selection.
- `answer` absent means not yet answered. Carry the block forward unchanged.
- `answer: null` means asked and deliberately skipped. Treat it as "you decide", record the decision
  in the plan, and retire the block.

### Lint rules

`tendril plan write-revision` rejects a revision whose blocks break any of these, prints every
problem at once prefixed with the line of the opening fence, and writes nothing — so a rejected
revision does not consume a revision number. Fix the reported lines and retry.

| Rule | Message |
|---|---|
| At most one `recommended: true` per question | `question N: more than one option is recommended` |
| No hand-authored option titled "Other", "Something else", "Custom" | `question N: option '<title>' duplicates what other: true provides` |
| `other: false` with no options | `question N: other: false with no options is unanswerable` |
| `answer` is a list iff `multiple: true` | `question N: multiple: true requires a list answer` / `question N: answer must be a scalar when multiple is false` |
| `value` unique within a question | `question N: duplicate option value '<value>'` |
| `other: false` and an answer entry matches no option value | `question N: answer '<entry>' matches no option and other is false` |

Schema bounds are enforced too: 1-4 questions, a required `title`, a `header` of at most 12
characters, 2-4 options when `options` is present, a required `title` and slug `value` on each
option, and no unknown keys anywhere.

Blocks are numbered by position in the document, so a revision with several of them gets a
`block 1:` / `block 2:` prefix on top of the `question N:` prefix.

A `questions` fence written inside a longer fence is documentation, not a question — this document
is itself an example — so it is neither validated nor rendered.

**Legacy blocks.** A fence whose body is not a YAML mapping with a `questions` key is the plain-text
form that predates this schema. It produces a warning, never an error, and is never rewritten.

`--no-question-check` on `write-revision` skips this validation. It is the escape hatch for scripted
and test use; promptwares should never reach for it.

## Logs

`logs/{NNN}-{Action}.md` per promptware run (Completed time, status, …).

## .counter

Single integer in `{planFolder}/.counter`; managed by `tendril plan create`. Do not read or modify directly — always use the CLI.

## Verifications

Verifications live in `plan.yaml` (not in the revision markdown), each with a `Name` and a `Status` of `Pending | Pass | Fail | Skipped`. Every verification of the plan's project is seeded at creation, in the project's configured order (which is the order they run in). `Pending` = ExecutePlan will run it; `Skipped` = it won't. Users toggle Pending/Skipped from the Verifications card in the plan UI; the agent sets `Pass`/`Fail` via `tendril plan set-verification` after running each one. Definitions live in top-level `config.yaml` `verifications`; projects reference them by name + `required`.

## Notes

- **Local file links in plans:** `[filename:line](file:///path/to/filename)` for source files with a line number, or `[filename](file:///path/to/filename)` without. The line number belongs only in the display text — never append it to the URL itself (no `:348` suffix and no `#L123` fragment), or VS Code can't open the path. Never use backticks in link text. **Only link files that already exist** — for a file the plan will create, write its path in inline code (e.g. `` `src/New/Thing.cs` ``), not as a link; links to non-existent paths render broken.
- **Plan references:** `[Plan 03156](plan://03156)` to link to other plans. The link handler will navigate to that plan in the Plans app. The plan ID can be 5 digits (e.g., `plan://03156`) or without leading zeros (e.g., `plan://3156`).
- Images: normal markdown `![alt](url)`.
- **Diagrams:** Graphviz/DOT (```dot / ```graphviz) or Mermaid (```mermaid). **Prefer DOT** for layout. Use only when a diagram really helps.

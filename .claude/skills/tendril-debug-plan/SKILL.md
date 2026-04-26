---
name: tendril-debug-plan
description: Debug a Tendril plan by analyzing its execution logs, session JSONL, verification results, and checking infrastructure. Produces actionable bugfix and improvement recommendations. Use when the user wants to investigate why a plan failed, behaved unexpectedly, or to audit plan execution quality.
---

# tendril-debug-plan

Debug and analyze Tendril plan executions end-to-end — from plan creation through checking/verification — and produce a set of concrete bugfix and improvement recommendations.

## Invocation

```
/tendril-debug-plan <planid> <note>
```

* **planid** — 5-digit Tendril plan ID (e.g., `03451`)
* **note** — free-text context about what to look for (e.g., "verification passed but shouldn't have", "took forever", "got stuck in Building state")

## What This Skill Does

1. Gathers all artifacts for a plan: `plan.yaml`, revisions, logs, costs, verification reports, session JSONL
2. Analyzes the execution timeline, token usage, tool call patterns, and error loops
3. Cross-references findings with Tendril source code and promptware instructions
4. Produces a structured recommendations report with concrete fixes

## Execution Steps

### Phase 1 — Gather Plan Artifacts

Resolve paths from environment:

* `TENDRIL_HOME` — base config/data directory
* `TENDRIL_PLANS` — plans directory (defaults to `$TENDRIL_HOME/Plans`)
* `REPOS_HOME` — for locating Tendril source code

Read these files from the plan folder (`$TENDRIL_PLANS/{planid}-*/`):

| File                | Purpose                                                                                                    |
| ------------------- | ---------------------------------------------------------------------------------------------------------- |
| `plan.yaml`         | Plan metadata: state, repos, commits, PRs, verifications, dependsOn                                        |
| `revisions/*.md`    | Plan scope, acceptance criteria, verification checkboxes. Last one is the one that is the executable one.  |
| `logs/*.md`         | Per-step execution logs with outcome data (commits, verifications, cost). Also Tendril CLI logs.           |
| `costs.csv`         | Token/cost breakdown per promptware (if available)                                                         |
| `verification/*.md` | Verification reports (PreExecution, IvyFrameworkVerification, etc.)                                        |
| `worktrees/`        | Check if worktrees were created/cleaned up                                                                 |

Also check promptware-level logs:

* `$TENDRIL_HOME/Promptwares/{Type}/Logs/{PlanId}.md` — agent summary
* `$TENDRIL_HOME/Promptwares/{Type}/Logs/{PlanId}.raw.jsonl` — full session data

### Phase 2 — Locate and Analyze Session JSONL

Each log entry in `logs/` contains a `SessionId`. The raw Claude session data lives at:

```
~/.claude/projects/*/{SessionId}.jsonl
```

Use `find ~/.claude/projects -name "{SessionId}.jsonl"` to locate each file.

For each JSONL session, extract:

**Token Usage:**

* Sum `input_tokens`, `output_tokens`, `cache_read_input_tokens`, `cache_creation_input_tokens` from `type: "assistant"` messages
* Cache hit ratio: `cache_read / (cache_read + cache_creation + input)`
* Flag messages with unusually high `input_tokens` (context bloat)

**Tool Call Patterns:**

* Count each tool type (Read, Write, Edit, Bash, Grep, Glob)
* Identify repeated reads of the same file (redundant)
* Identify failed tool calls and their errors
* Detect thrashing: read-edit-read-edit cycles on the same file

**Error Patterns:**

* Grep for `error`, `failed`, `exception`, `timeout` in tool results
* Count compilation fix-retry cycles (build → error → edit → build loops)
* Permission errors, missing files, environmental issues

**Time Analysis:**

* Wall-clock duration from first to last timestamp
* Long gaps between messages (slow tools, rate limiting)
* Timeout detection

Use the `Analyze-SessionJsonl.ps1` tool if available at:

```
$REPOS_HOME/Ivy-Tendril/src/Ivy.Tendril.TeamIvyConfig/Promptwares/PlanEvaluator/Tools/Analyze-SessionJsonl.ps1
```

### Phase 3 — Analyze the Checking/Verification Pipeline

This is the core debugging focus. Examine:

**Pre-execution checks (ExecutePlan Step 1.5–1.8):**

* Did dependency checking work correctly? (`dependsOn` plans completed, PRs merged)
* Did worktree validation catch problems? Or miss them?
* Did code state validation (`**Current implementation**` blocks) match reality?
* Did auto-commit handle dirty files properly?

**Verification execution (ExecutePlan Step 7):**

* Which verifications ran vs were skipped?
* Did verifications match what the plan revision checkboxes specified?
* For each verification: did the prompt execute correctly? Were failures diagnosed?
* How many fix-retry cycles occurred (max 3 allowed)?
* Were verification results written to `verification/` correctly?
* Were plan verification statuses updated via `tendril plan set-verification`?

**Post-verification (ExecutePlan Step 7.5–8):**

* Were recommendations generated?
* Was the worktree left clean?
* Were zombie processes detected/killed?

**CheckResult / completion verification (JobService):**

* For CreatePlan: did `VerifyCreatePlanResult` find the plan folder or trash entry?
* Did `CheckDependencies` correctly evaluate dependency plan states?
* Did `TryBlockForDependencies` transition appropriately?

Cross-reference each finding with:

* `Promptwares/{Type}/Program.md` — could instructions prevent this?
* `Promptwares/{Type}/Memory/` — is knowledge missing or ignored?
* `Services/JobService.cs` — job lifecycle issues
* `Services/PlanReaderService.cs` — plan state/repair issues
* `Promptwares/.shared/Plans.md` — schema/CLI issues

### Phase 4 — Produce Recommendations Report

Write the report to `$TENDRIL_PLANS/{planid}-*/debug-report.md` (alongside the plan).

Use this format:

```Markdown
# Debug Report: {PlanId} — {Title}

- **Analyzed:** {current timestamp}
- **Plan State:** {state}
- **User Note:** {the note argument}
- **Promptwares Run:** {list}
- **Total Tokens:** {sum}
- **Wall-Clock Time:** {duration}

## Executive Summary

{3-5 sentences: what happened, what went wrong, what's the root cause}

## Timeline

| # | Step | Promptware | Status | Duration | Tokens | Notes |
|---|------|------------|--------|----------|--------|-------|
| 1 | CreatePlan | CreatePlan | Completed | 2m30s | 45k | — |
| 2 | Execute | ExecutePlan | Failed | 15m | 280k | build loop |
| ... | | | | | | |

## Checking & Verification Analysis

### Pre-Execution Checks
{What passed, what failed, what was missed}

### Verification Results
| Verification | Expected | Actual | Correct? | Notes |
|-------------|----------|--------|----------|-------|
| Build | Pass | Pass | Yes | — |
| IvyFramework | Pass | Pass | No | Should have caught X |

### Completion Verification
{How JobService verified the result, any gaps}

## Findings

### {Finding Title}

- **Category:** {Token Waste | Error Loop | Missing Knowledge | Instruction Gap | Environmental | Architectural | Verification Gap}
- **Severity:** {Low | Medium | High | Critical}
- **Promptware:** {which one}
- **Evidence:** {specific log lines, timestamps, tool call IDs}

{Description with specific evidence.}

**Root Cause:** {why this happened}

**Recommendation:** {concrete fix — which file to change, what to change, why}

---

{Repeat for each finding}

## Concrete Fixes

Priority-ordered list of specific changes:

1. **[High] {file path}**: {what to change and why}
2. **[Medium] {file path}**: {what to change and why}
3. ...

## Skill Self-Improvement Notes

{If this analysis revealed patterns or techniques that would make future debugging faster,
note them here. These will be incorporated into the skill's references/ directory.}
```

## Key Tendril Files for Cross-Reference

These are the files most likely to contain the root cause of issues:

| File                                               | What It Controls                                            |
| -------------------------------------------------- | ----------------------------------------------------------- |
| `Services/JobService.cs`                           | Job lifecycle, dependency checking, completion verification |
| `Services/JobLauncher.cs`                          | Job launch, CLI shim generation, firmware value population  |
| `Services/JobCompletionHandler.cs`                 | Post-completion: logs, raw output, plan state, telemetry    |
| `Services/PlanReaderService.cs`                    | Plan state transitions, repair logic, stuck plan recovery   |
| `Services/GitService.cs`                           | Worktree creation/cleanup, commit operations                |
| `Services/Agents/FirmwareCompiler.cs`              | Prompt compilation, log file allocation                     |
| `Promptwares/ExecutePlan/Program.md`               | Main execution flow, all verification steps                 |
| `Promptwares/CreatePlan/Program.md`                | Plan creation, folder setup                                 |
| `Promptwares/.shared/Plans.md`                     | Plan schema, CLI commands, state lifecycle                  |
| `Promptwares/{Type}/Memory/`                       | Agent knowledge base per promptware                         |
| `Models/PlanModels.cs`                             | Plan deserialization model                                  |
| `Helpers/PlanContentHelpers.cs`                    | Commit row building, plan content rendering                 |

## Rules

* **Read-only by default**: do NOT modify source code, promptware instructions, or memory files during analysis. The output is a recommendations report.
* **Always produce a report**, even if no issues are found — "plan executed cleanly" is a valid finding.
* **Be specific**: cite file paths, line numbers, log timestamps, tool call sequences.
* **Focus on the checking pipeline**: verification gaps (things that should have been caught but weren't) are higher priority than token waste.
* **Use targeted reads**: JSONL files can be huge — use offset/limit or grep rather than reading entire files.
* **The user's note is your guide**: prioritize investigating what the user flagged.

## Self-Evolution

This skill is designed to improve over time. After producing a report:

1. If you discovered a new debugging pattern or common failure mode not covered here, write it to `references/{topic}.md`
2. If a cross-reference path was wrong or a file moved, update the paths in this SKILL.md
3. If the report format could be improved based on what you learned, update the template above

The `references/` directory accumulates knowledge from past debugging sessions.

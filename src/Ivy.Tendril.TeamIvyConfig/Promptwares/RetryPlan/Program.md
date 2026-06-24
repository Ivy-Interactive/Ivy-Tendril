# RetryPlan

**Note:** This promptware is stack-agnostic. Stack-specific operations (build, format, test) are defined as verifications in the project configuration. Examples in this document use multiple tech stacks for illustration.

Implement improvements on an already-executed plan, incorporating reviewer feedback. Works in the **existing worktree** created by ExecutePlan — does NOT create or destroy worktrees.

## Context

The firmware header contains:

- **TendrilPlanFolder** — path to the plan folder
- **CurrentTime** — current UTC timestamp
- **ChangeRequest** — Reviewer feedback describing what needs to change. Address this feedback as your primary objective.

The plan structure and CLI commands are in the **Reference Documents** section of your firmware.
Project repos, verifications, and context are in the **Projects** section of your firmware. Use `tendril verification get <name>` to fetch the full prompt for each verification at execution time.

The launcher sets the working directory to the project's primary repo.

## Change Request Priority

The `ChangeRequest` header contains specific changes the reviewer wants. Your primary objective is to address this feedback.

Read the ChangeRequest carefully before starting implementation. The original plan revision still defines the scope, but the ChangeRequest takes priority for any conflicting instructions.

## Execution Steps

### 1. Read Plan

- Read `plan.yaml` from the plan folder (project, repos, title)
- Read the latest revision from `Revisions/` (highest numbered .md file)
- Extract the plan ID from the folder name (e.g. `00012` from `00012-AddNewsletterSignupToHelpApp`)
- Report plan context to Jobs UI: `tendril job status TendrilJobId --message="Retrying plan..." --plan-id=<plan-id> --plan-title="<title>"`

### 2. Enter Existing Worktrees

The worktrees were created by the prior ExecutePlan run and already contain branches with previous commits. A project may consist of multiple repos — each has its own worktree under `<TendrilPlanFolder>/Worktrees/`.

For each repo in `plan.yaml` `repos` (or the project's repos from the **Projects** section if empty):

1. **Locate the worktree** at `<TendrilPlanFolder>/Worktrees/<repo-folder-name>`.

2. **Verify it exists:**

```bash
if [ ! -d "<TendrilPlanFolder>/Worktrees/<repo-folder-name>" ]; then
    echo "ERROR: Worktree not found at <TendrilPlanFolder>/Worktrees/<repo-folder-name>"
    echo "RetryPlan requires an existing worktree from a prior ExecutePlan run."
    echo "Re-run ExecutePlan first, or check that worktrees were not cleaned up prematurely."
    exit 1
fi
```

3. **Verify it's a valid git worktree** (has a `.git` file):

```bash
if [ ! -f "<TendrilPlanFolder>/Worktrees/<repo-folder-name>/.git" ]; then
    echo "ERROR: Directory exists but is not a valid git worktree (.git file missing)"
    exit 1
fi
```

4. **Switch to the worktree directory** — all subsequent work happens here.


### 3. Implement Changes

Work exclusively in the worktree directories. Address the **ChangeRequest** feedback:

1. **ChangeRequest** — Address the reviewer's feedback. This is why this re-execution was triggered.
2. **Problem** — Understand what needs to be done (from the plan revision) for context
3. **Solution** — Implement the changes in the worktree, incorporating the ChangeRequest modifications
4. **Tests** — Write and run any tests specified

### 4. Commit

Make logically grouped commits in the worktree(s). Each commit should be a coherent unit of work.

Before each commit, run formatting/linting as defined by the project's verifications. Fetch the full prompt for a verification with `tendril verification get <name>`.

Write clear commit messages describing the change:

```
Improve error handling per review feedback
```

After all commits, verify no uncommitted files remain:

```bash
git status
```

If there are uncommitted changes, either commit them or discard them with a clear reason. The worktree must be clean.

### 4.5. Update Summary

The prior ExecutePlan run created `<TendrilPlanFolder>/Artifacts/summary.md`. **Do not replace it** — append a new section documenting this retry's changes:

~~~markdown
## Fix: <short description>

<What was changed and why, referencing the ChangeRequest. 2-3 sentences.>

### Files Modified

<Bulleted list of files changed in this retry.>
~~~

Add one such section per logical fix. If the retry addresses multiple items from the ChangeRequest, add a section for each.

### 5. Document Commits

Use the CLI to record commits — **never edit plan.yaml directly**.

Add each commit hash:

```bash
tendril plan add-commit <plan-id> abc1234
tendril plan add-commit <plan-id> def5678
```

Verification statuses already live in `plan.yaml`. Do **not** derive them from the plan revision — there is no `## Verification` section. You only update each verification's status to `Pass`/`Fail` after running it (Step 6).

**CRITICAL:** The `tendril plan add-commit` and `tendril plan set-verification` CLI commands are the ONLY mechanism that updates plan.yaml. You MUST call these commands.

### 6. Run Verifications

Create a `Verification/` directory in the plan folder if it doesn't exist.

Get the run-set via `tendril plan verification list <plan-id> --json` — it emits a JSON array of `{ name, status }` **in run order**. Run the entries whose `status` is `Pending`, in array order. Skip entries whose `status` is `Skipped`.

**Delegated verifications:** Some verifications are implemented as separate promptwares. The **Projects** section marks delegated verifications. Delegated verifications MUST be run via `tendril promptware run <Name>` — you are FORBIDDEN from writing their report files or setting their status to Pass yourself.

For each `Pending` verification (in listed order):

1. Send a status message: `tendril job status TendrilJobId --message="Verifying: <Name>"`
2. Fetch its full prompt: `tendril verification get <Name>`
3. **Check if delegated:** Follow the prompt's instructions to invoke it as an external process if delegated.
4. Execute the prompt in the worktree directory
5. If it fails: diagnose, fix the issue, **commit the fix**, and re-run. Repeat until it passes (fail the plan after 3+ failed attempts).
6. Document all fix commits via CLI: `tendril plan add-commit <plan-id> <sha>`
7. Update the verification status via CLI: `tendril plan set-verification <plan-id> <Name> Pass` (or `Fail`)

**CRITICAL:** You MUST call `tendril plan set-verification` after EACH verification.

**Every verification MUST produce a report** at `<TendrilPlanFolder>/Verification/<VerificationName>.md` using YAML frontmatter:

```markdown
---
result: Pass
date: <CurrentTime>
attempts: <number>
---
# <VerificationName>

## Output

<command output or summary>

## Fixes Applied

<list of fix commits made during this verification, or "None">

## Issues Found

<any remaining issues, or "None">
```

### 6.5. Generate Recommendations

After all verifications pass, write down anything you noticed that isn't part of this plan's scope:

- Follow-up work, edge cases not covered, or related features
- Confusing code, inconsistent patterns, or technical debt
- Unrelated bugs, broken tests, or incorrect behavior
- Performance improvements or refactoring opportunities

For each item, register it via the CLI:

```bash
tendril plan rec add <plan-id> "Short descriptive title" -d "Markdown description with context and location." --impact=Medium --risk=Small
```

**After registering recommendations**, create `<TendrilPlanFolder>/Artifacts/recommendations.md`:

~~~markdown
# Recommendations

## Items

- **<Title>** — <one-line summary>

*Or: "None — <one sentence explaining why>"*
~~~

**This file is mandatory.** Step 7 will verify it exists.

### 7. Final Clean Check

After all verifications pass:

1. Kill any remaining processes spawned during plan execution (e.g. dev servers) whose working directory is under the plan's worktree or artifacts directory. **See Prohibited Actions below — never kill dotnet.exe or Ivy.Tendril.exe.**

2. Run `git status` in every worktree. If there are uncommitted files, commit or discard them. The worktrees must be completely clean.

3. Verify `<TendrilPlanFolder>/Artifacts/recommendations.md` exists. If missing, go back to Step 6.5.

### 8. Plan State

The launcher script handles state transitions (Completed/Failed) based on exit code.

## Prohibited Actions

- **NEVER kill `dotnet.exe` or `Ivy.Tendril.exe` processes.** Tendril (your orchestrator) is a .NET application hosted by `dotnet.exe`. Killing it will terminate Tendril itself, losing all job state.
- **NEVER create or destroy worktrees.** RetryPlan works in the existing worktree. If the worktree is missing, fail immediately.
- Do NOT commit artifact files (screenshots, images) to the repo. Test artifacts belong in `<TendrilPlanFolder>/Artifacts/` only.
- Do NOT create filesystem aliases or shortcuts (symlinks, drive mappings) to worktree paths.

## Ambiguity Handling

You are running in non-interactive mode and CANNOT ask questions. If you are unsure about requirements, encounter conflicting instructions, or cannot find referenced files — STOP and fail with a clear message explaining what needs clarification. Do NOT guess when uncertain.

## Rules

- All work happens in worktree directories, never in the original repos
- Make logically grouped commits — not one giant commit
- Worktrees must be clean (no uncommitted files) when finished
- Document all commit hashes via `tendril plan add-commit` — never edit plan.yaml directly
- Follow the plan instructions exactly as written, with ChangeRequest taking priority
- Do NOT skip tests or pre-commit formatting
- Commit messages must reference the plan ID
- Convert `file:///` paths in plans to local filesystem paths appropriate for your OS
- If the project uses private package registries, ensure authentication is configured before running dependency installation in worktrees

# RetryPlan

**Note:** This promptware is stack-agnostic. Stack-specific operations (build, format, test) are defined as verifications in the project configuration. Examples in this document use multiple tech stacks for illustration.

Implement improvements on an already-executed plan, incorporating reviewer feedback. Works in the **existing worktree** created by ExecutePlan, resuming from where that run left off rather than starting over.

**Resume, do not redo.** The worktree already contains the previous run's implementation and commits. The `ChangeRequest` is a **delta** on top of that work, not a fresh start. Never re-implement a part of the plan that is already in the code, never revert, amend, squash or force-push existing commits, and never delete the worktree branch. Verify what exists first (Step 2.5), then change only what is missing or wrong.

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
- Read the latest revision: `tendril plan get-revision <TendrilPlanId>`
- Extract the plan ID from the folder name (e.g. `00012` from `00012-AddNewsletterSignupToHelpApp`)
- Report plan context to Jobs UI: `tendril job status TendrilJobId --message="Retrying plan..." --plan-id=<plan-id> --plan-title="<title>"`

### 2. Enter or Recover Worktrees

Report status: `tendril job status TendrilJobId --message="Entering worktrees..."`

The worktrees were created by the prior ExecutePlan run and already contain branches with previous commits. A project may consist of multiple repos — each has its own worktree under `<TendrilPlanFolder>/Worktrees/`.

For each repo in `plan.yaml` `repos` (or the project's repos from the **Projects** section if empty), resolve the worktree in this order:

1. **Worktree directory exists and is valid.** `<TendrilPlanFolder>/Worktrees/<repo-folder-name>` exists and contains a `.git` file: use it as-is. Confirm it is actually attached rather than a stale leftover: `git -C <main-repo> worktree list` must list this path. Running `git status` inside a stale directory silently reports the main repo's state instead of failing, which can look healthy when it is not.

2. **Directory missing, but the branch still exists.** Check the main repo for the plan's own branch, and for the head branch of any PR listed in plan.yaml `prs` or `SourceUrl` when the plan updates an existing PR:

```bash
git -C <main-repo> branch --list "tendril/<planFolderName>"
```

   If a branch is found, re-attach the worktree without touching its history:

```bash
git -C <main-repo> worktree add "<TendrilPlanFolder>/Worktrees/<repo-folder-name>" "<existing-branch>"
```

   Do not use `tendril plan add-worktree` here: it always creates a new branch from the base branch, which would fail or cut a fresh branch over the prior work instead of reusing it.

3. **Neither the directory nor a branch exists.** The prior run died before creating anything to resume. Create a fresh worktree, report via `tendril job status` that no prior work was recoverable, and implement the revision in full:

```bash
tendril plan add-worktree <plan-id> <repo-path> [--base <baseBranch from RepoConfigs>]
```

   Note this in the Step 4.5 summary update: this run implemented the plan from scratch.

After cases 2 and 3, verify `<TendrilPlanFolder>/Worktrees/<repo-folder-name>/.git` exists:

```bash
if [ ! -f "<TendrilPlanFolder>/Worktrees/<repo-folder-name>/.git" ]; then
    echo "ERROR: Worktree recovery failed - .git file missing"
    exit 1
fi
```

4. **Switch to the worktree directory.** All subsequent work happens here.

### 2.5. Assess Prior Work

Report status: `tendril job status TendrilJobId --message="Assessing prior work..."`

Before changing anything, gather what the previous run already did:

- `tendril plan get <plan-id> commits`: the commits the prior run recorded.
- In each worktree: `git log --oneline -n 30`, `git status --short`, and `git diff --stat "origin/<baseBranch>"...HEAD` for the full set of changes already made.
- `<TendrilPlanFolder>/Artifacts/summary.md`, if present: the prior run's own account of what it did.
- Every `<TendrilPlanFolder>/Verification/*.md`: the `result` in the frontmatter, plus its "Fixes Applied" and "Issues Found" sections. The server resets every non-Skipped verification in plan.yaml back to `Pending` before this job starts, so these report files are the only surviving record of what previously passed and what was already fixed.

From this, build a short mental ledger with three buckets: **already delivered** (present in the code and committed), **still missing** from the revision's Solution, and **to change** per the ChangeRequest. Confirm each "already delivered" item against the actual files, not against the plan text or the summary; a summary can describe work that a later crash left unfinished.

### 3. Implement Changes

Report status: `tendril job status TendrilJobId --message="Implementing changes..."`

Work exclusively in the worktree directories, using the ledger from Step 2.5:

1. Apply the **ChangeRequest** items. This is why this re-execution was triggered.
2. Apply only the **Solution** items the ledger marks as still missing.
3. Skip anything the ledger marks as already delivered. If a file, function, or test already exists, edit it rather than recreating it, and never duplicate an existing test.
4. Add new commits on top of the existing history. When the ChangeRequest asks for something to be undone, do that as a new commit, not by rewriting history.

### 4. Commit

Report status: `tendril job status TendrilJobId --message="Committing changes..."`

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

Inspect any uncommitted changes before deciding what to do with them: after a crashed or stopped run they are usually unfinished implementation, so finish and commit them. Discard only files that are clearly build, dependency, or test debris, and state what was discarded and why in the status message. The worktree must be clean.

### 4.5. Update Summary

The prior ExecutePlan run created `<TendrilPlanFolder>/Artifacts/summary.md`. **Do not replace it** — append a new section documenting this retry's changes:

~~~markdown
## Fix: <short description>

<What was changed and why, referencing the ChangeRequest. 2-3 sentences.>

### Files Modified

<Bulleted list of files changed in this retry.>

**Note:** Update the manual testing section if this fix affects user-facing behavior.
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

Before running a verification, read its previous report at `<TendrilPlanFolder>/Verification/<VerificationName>.md` if one exists. If it recorded a fix, confirm the fix is still committed rather than redoing it. If it left issues open, start the fix loop from those issues instead of from scratch.

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

### 7. Final Clean Check

Report status: `tendril job status TendrilJobId --message="Running final checks..."`

After all verifications pass:

1. Kill any remaining processes spawned during plan execution (e.g. dev servers) whose working directory is under the plan's worktree or artifacts directory. **See Prohibited Actions below — never kill dotnet.exe or Ivy.Tendril.exe.**

2. Run `git status` in every worktree. If there are uncommitted files, commit or discard them. The worktrees must be completely clean.

### 8. Plan State

The launcher script handles state transitions (Completed/Failed) based on exit code.

## Prohibited Actions

- **NEVER kill `dotnet.exe` or `Ivy.Tendril.exe` processes.** Tendril (your orchestrator) is a .NET application hosted by `dotnet.exe`. Killing it will terminate Tendril itself, losing all job state.
- **NEVER destroy or reset an existing worktree, its branch, or its commits.** Create a worktree only when both the directory and its branch are missing (Step 2, case 3).
- Do NOT commit artifact files (screenshots, images) to the repo. Test artifacts belong in `<TendrilPlanFolder>/Artifacts/` only.
- Do NOT create filesystem aliases or shortcuts (symlinks, drive mappings) to worktree paths.

## Ambiguity Handling

You are running in non-interactive mode and CANNOT ask questions. If you are unsure about requirements, encounter conflicting instructions, or cannot find referenced files — STOP and fail with a clear message explaining what needs clarification. Do NOT guess when uncertain.

**Unanswered question blocks.** Before doing any work, scan the plan revision you read  for any fenced `questions` block (see the **Question Blocks** section of **Reference Documents**).

An unanswered question is **not** a failure. The user saw the block and chose not to answer, which is a decision in itself: it means *you* decide. Resolve each one yourself, in this order:

1. **Take the `recommended` option** if the question has one. It is the asking agent's own choice, made with the plan in front of it.
2. **Otherwise pick the most reasonable answer** from the options, or — for a free-text question — from the plan and the code. Prefer the option that is smallest in scope and easiest to change later.

Record every question you resolved this way in the execution log with the answer you picked and the one-line reason, so the decision is auditable and the next revision can fold it in. Then continue.

A question that already carries an `answer` is a decision the user made: honor it exactly, and never re-decide it. A block that is fully answered but still present — the user retried without running UpdatePlan first — is likewise not a failure. RetryPlan never writes revisions, so it never adds or edits question blocks.

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

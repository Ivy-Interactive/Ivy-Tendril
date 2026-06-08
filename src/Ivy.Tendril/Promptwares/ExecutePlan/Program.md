# ExecutePlan

**Note:** This promptware is stack-agnostic. Stack-specific operations (build, format, test) are defined as verifications in the project configuration. Examples in this document use multiple tech stacks for illustration.

Execute an approved plan in isolated git worktrees.

## Context

The firmware header contains:

- **TendrilPlanFolder** — path to the plan folder
- **CurrentTime** — current UTC timestamp
- **Note** (optional) — Additional instructions from the reviewer. If present, follow these instructions in addition to the plan.

The plan structure and CLI commands are in the **Reference Documents** section of your firmware.
Project repos, verifications, and context are in the **Projects** section of your firmware. Use `tendril verification get <name>` to fetch the full prompt for each verification at execution time.

The launcher sets the working directory to the project's primary repo.

**Note:** Plans are often executed multiple times. For example, a reviewer may not be satisfied with the first execution and sends the plan back to Draft with comments (via UpdatePlan). When re-executing, the worktree branch from the previous run may already exist — handle this gracefully (delete old worktree first, or create with a new branch suffix). Check for existing artifacts and verification reports from prior runs.

**Resume-vs-redo on re-execution:** Before deleting anything, run an integrity check on the prior run. If `plan.yaml` has commits populated and all verifications `Pass`, every `Pass` verification has a report, `Artifacts/summary.md` exists, the worktree is clean with HEAD matching the last recorded commit, and the expected code changes are present in the files — then **resume** (log it and exit successfully) rather than redoing work. Redoing creates new commit hashes and breaks downstream CreatePr references. Only fall back to the full re-execution flow if any of those checks fail.

## Time Budget Awareness

**You have a 30-minute hard timeout.** Plan your time carefully:

1. **Spend at most 10 minutes reading/understanding the codebase**, then start implementing. If you haven't started writing code by the 10-minute mark, simplify your approach.

2. **Prefer implementing incrementally** (write code, build, fix errors) over exhaustive upfront research. You can read more code as needed during implementation.

3. **If the plan involves unfamiliar patterns, look at ONE good example and follow it** — don't survey every usage in the codebase. Find a single clear reference and proceed.

Focus on making progress, not achieving perfect understanding. A working implementation with minor imperfections beats a timeout with no code written.

## Execution Steps

### 1. Read Plan

- Read `plan.yaml` from the plan folder (project, repos, title)
- Read the latest revision from `Revisions/` (highest numbered .md file)
- Extract the plan ID from the folder name (e.g. `01105` from `01105-TestPlan`)
- Report plan context to Jobs UI: `tendril job status TendrilJobId --message "Reading plan..." --plan-id <plan-id> --plan-title "<title>"`

### 1.5. Verify Dependencies

Report status: `tendril job status TendrilJobId --message "Checking dependencies..."`

If `plan.yaml` has a `dependsOn` list, for each entry:

1. Locate the dependency plan folder in the plans directory
2. Verify the dependency plan's state is `Completed`
3. Verify all PRs listed in the dependency's `plan.yaml` are actually merged on GitHub:

   ```bash
   gh pr view <pr-url> --json state -q .state
   # Must return "MERGED"
   ```

4. If any dependency is unmet (not completed or PRs not merged), **fail immediately** with a clear message explaining which dependency isn't ready and why.

**Note:** The JobService also performs this check before launching ExecutePlan, but this step acts as a safety net in case the dependency state changed between job launch and execution.

### 1.6. Validate Worktree Isolation

Before creating worktrees, verify the execution environment is safe:

1. **Check each repo is not itself a worktree** — If `<repo-path>/.git` is a file containing `gitdir:`, the repo is a worktree. Fail with error:
   > ERROR: Repository at <repo-path> is itself a worktree. ExecutePlan cannot create worktrees inside worktrees. Update project configuration to use the main repo path.

2. **Check Plans directory is not inside a worktree** — If `$TENDRIL_HOME` or its parent contains a worktree `.git` file, fail with error:
   > ERROR: TENDRIL_HOME is inside a git worktree. Move your Tendril installation or change the Plans directory.

```bash
# For each repo in plan.yaml repos (or project repos if empty):
cd <repo-path>

# Check if current directory is a worktree
if [ -f .git ] && grep -q "gitdir:" .git; then
    echo "ERROR: Repository at <repo-path> is itself a worktree."
    echo "ExecutePlan cannot create worktrees inside worktrees."
    echo "Check that project repo paths point to main repositories, not worktrees."
    exit 1
fi

# Check if Plans directory would be created inside a worktree
PLANS_DIR_PARENT=$(dirname "$TENDRIL_HOME")
cd "$PLANS_DIR_PARENT"
if git rev-parse --is-inside-work-tree 2>/dev/null && [ -f "$PLANS_DIR_PARENT/.git" ]; then
    if grep -q "gitdir:" "$PLANS_DIR_PARENT/.git"; then
        echo "ERROR: TENDRIL_HOME ($TENDRIL_HOME) is inside a git worktree."
        echo "Plans and their worktrees cannot be created inside worktrees."
        echo "Move your Tendril installation outside the worktree or use a different Plans directory."
        exit 1
    fi
fi
```

This prevents recursive worktree scenarios that would corrupt git state and cause massive repo bloat.

### 1.7. Validate Code State

Report status: `tendril job status TendrilJobId --message "Validating code state..."`

After reading the plan revision, scan it for code validation markers to detect stale plans (where the described code has already been changed by another plan).

1. **Extract validation blocks** — Parse the plan revision for sections containing:
   - Headers matching `**Current implementation**`, `**Current implementation in <file>**`, or `**Old implementation**`
   - Fenced code blocks (` ```language ... ``` `) immediately following these headers
   - Associated file paths (markdown links with `file:///` or inline text like `helpers.py:217`)

2. **Validate code exists** — For each validation block found:
   - Extract the file path from the context (header text or preceding paragraph)
   - Convert `file:///` URLs to local paths if needed
   - If a line range is specified (e.g., `:217-242`), read those specific lines
   - Otherwise, read the entire file and search for the code snippet (normalize whitespace when comparing — ignore leading/trailing blank lines and trailing spaces)
   - **Exact match** → validation passes, proceed
   - **Not found** → validation fails, the code may have already changed
   - **File not found** → validation fails, the file may have been deleted/moved

3. **Decision logic:**
   - **If no validation blocks found** → Skip validation, proceed to worktree creation (backward compatible)
   - **If all validation blocks pass** → Proceed to worktree creation
   - **If any validation fails** → Fail the plan immediately with a detailed report

4. **Write validation report** — Create `<TendrilPlanFolder>/Verification/PreExecution.md`:

```markdown
# PreExecution

- **Date:** <CurrentTime>
- **Result:** Pass / Fail / Skipped
- **Blocks Found:** <number>

## Validation Blocks

### Block 1: <file path>
- **Status:** Pass / Fail
- **Expected:** (first 5 lines of expected code)
- **Actual:** (first 5 lines of actual code, or "File not found")

## Recommendation (on failure)

- Review the plan against the current codebase
- Check if this work was already completed by another plan
- Update the plan via UpdatePlan or mark as Skipped
```

**Note:** This step runs against the original repo (before worktrees are created), since it validates whether the plan's assumptions about the codebase are still accurate.

5. **Self-flagged redundancy check** — In addition to code block validation, scan the plan revision for markers where the plan itself admits it is already done:
   - A `<details><summary>Still relevant?</summary>` block whose body starts with `No.`
   - Phrases like *"Already applied"*, *"This plan is redundant"*, *"This plan is superseded"*, or *"previously attempted … was merged to main via PR #NNNN"* in the `## Problem` or `## Solution` sections.

   If any marker is found, verify the claim: run `gh pr view <cited PR> --json state,mergeCommit` (must be `MERGED`), confirm the cited commit is in `git log origin/<default-branch>`, and byte-compare the plan's proposed code against the current file contents. If all three checks pass, write `Verification/PreExecution.md` with `Result: Fail`, write `Artifacts/summary.md` documenting the no-op, set every verification to `Skipped` via `tendril plan set-verification <plan-id> <name> Skipped --job-id TendrilJobId`, and fail the plan **without creating a worktree** — running verifications on unchanged code wastes the time budget and produces a 0-commit PR that CreatePr cannot process.

### 2. Create Worktrees

Report status: `tendril job status TendrilJobId --message "Creating worktrees..."`

For each repo in `RepoConfigs` (this includes both the plan's repos AND any read-only build dependencies from the project config):

1. Fetch latest from remote: `git fetch origin`
2. Determine the base branch:
   - Check the `RepoConfigs` firmware header for this repo's `baseBranch` value
   - If configured, use that value as the base branch
   - Otherwise, auto-detect via: `git symbolic-ref refs/remotes/origin/HEAD | sed 's|refs/remotes/origin/||'`
3. If the worktree or branch already exists from a prior execution, remove it first:

```bash
tendril plan remove-worktree <TendrilPlanId> <repo-folder-name> --job-id TendrilJobId
```

This handles stale directories, locked files, and branch cleanup automatically with fallback strategies.

**Note on stale directories:** If a stale worktree directory exists and you run `git -C <stale-dir> status`, git silently walks up the parent chain and reports the state of the main repo — making it look like the "worktree" is simply on `main`. Do not trust that output. Before assuming a prior worktree is intact, verify with `git -C <main-repo> worktree list | grep <path>` or check that `<worktree-path>/.git` exists.

1. Create worktree branching from the remote default branch:

```bash
cd <original-repo-path>
git fetch origin
PLAN_FOLDER_NAME=$(basename "<TendrilPlanFolder>")
PLAN_ID=$(echo "$PLAN_FOLDER_NAME" | grep -oP '^\d+')
SAFE_TITLE=$(echo "$PLAN_FOLDER_NAME" | sed 's/^[0-9]\+-//')
BRANCH_NAME="tendril/$PLAN_ID-$SAFE_TITLE"
git worktree add "<TendrilPlanFolder>/Worktrees/<repo-folder-name>" -b "$BRANCH_NAME" "origin/<resolved-base-branch>"
```

Example:

```bash
cd <RepoPath>
git fetch origin
git worktree add "<TendrilPlanFolder>/Worktrees/<RepoName>" -b "tendril/<TendrilPlanId>-<SafeTitle>" origin/<resolved-base-branch>
```

**Important:** Always branch from `origin/<resolved-base-branch>`, not local HEAD. This ensures the PR only contains the plan's commits, not any unpushed local work. The `<resolved-base-branch>` comes from either the `RepoConfigs` firmware header (if `baseBranch` is configured) or auto-detection.

**Note on `RepoConfigs`:** The firmware header may include a `RepoConfigs` value injected by Tendril. It contains per-repo configuration:
```yaml
RepoConfigs: |
  - path: /home/user/repos/my-project
    baseBranch: main
    prRule: yolo
  - path: /home/user/repos/shared-lib
    baseBranch: main
    prRule: default
    readOnly: true
```
If `baseBranch` is present for a repo, use it instead of auto-detecting. If absent, fall back to `git symbolic-ref refs/remotes/origin/HEAD`.

**Read-only repos** (`readOnly: true`) are build dependencies — they need worktrees so that cross-repo project references resolve, but you must NOT make changes, commits, or PRs in them. Create their worktrees the same way (branching from `origin/<baseBranch>`), but skip them during implementation steps 3-5.

4. After creating the worktree, **verify the `.git` file exists** and fail fast if it's missing:

```bash
if [ ! -f "<TendrilPlanFolder>/Worktrees/<repo-folder-name>/.git" ]; then
    echo "ERROR: Worktree creation failed - .git file missing at <TendrilPlanFolder>/Worktrees/<repo-folder-name>/.git"
    echo "This indicates git worktree add did not fully initialize the worktree."
    exit 1
fi
cat "<TendrilPlanFolder>/Worktrees/<repo-folder-name>/.git"
```

This ensures ExecutePlan fails immediately if worktree creation is incomplete, rather than leaving orphaned directories that trigger warnings during cleanup.

### 2.5. Setup Build Dependencies in Worktrees

**Note:** This section applies only when the project has build-time dependencies (e.g. frontend packages, generated code, pre-built artifacts) that need special handling in worktrees. Skip if not applicable.

If this step applies, report status: `tendril job status TendrilJobId --message "Setting up build dependencies..."`

Worktrees start with a clean checkout and may be missing build artifacts (e.g. `dist/`, `node_modules/`, generated files) that exist in the original repo. Determine whether the plan modifies these areas:

#### Default Path (No Changes to Build-Dependent Code)

If the plan does **NOT** modify code in directories with build artifacts:

1. **Copy pre-built artifacts** from the original repo into the worktree to avoid unnecessary rebuilds:

```bash
# Example: copy dist/ directories from original repo to worktree
for artifact_dir in $(find "<original-repo-path>" -name "dist" -type d -not -path "*/node_modules/*"); do
  relative_path="${artifact_dir#<original-repo-path>/}"
  parent_dir=$(dirname "$relative_path")
  mkdir -p "<worktree-path>/$parent_dir"
  cp -r "$artifact_dir" "<worktree-path>/$parent_dir/"
done
```

2. **Skip dependency installation** — the copied artifacts are sufficient for build and tests.

#### Exception Path (Build-Dependent Code Changes)

If the plan **modifies** build-dependent code, you MUST rebuild:

1. **Install dependencies** using the project's package manager
2. **Run the build** to regenerate artifacts
3. If dependency installation fails after 2 attempts, document the failure and fail the plan

### 3. Handle Cross-Repo References

Projects may reference other repos via absolute paths in project files (e.g. build files, module manifests, package configs).

These paths point to the original repos, not the worktree copies. Since we only modify files in the worktree, this is usually fine — the build references the original (stable) code.

**Do NOT modify project reference paths.** If a build fails because of cross-repo references, work around it by building from the worktree directory which inherits the original's references.

### 4. Implement

Report status: `tendril job status TendrilJobId --message "Implementing: <plan title>"`

Work exclusively in the worktree directories. Follow the plan's latest revision:

1. **Problem** — Understand what needs to be done
2. **Solution** — Execute the implementation steps in the worktree
3. **Tests** — Write and run all tests specified in the plan

**Status cadence:** During implementation, if any sub-task takes longer than 90 seconds, issue an intermediate status update describing the current activity (e.g., `"Implementing: writing tests..."`, `"Implementing: fixing lint errors..."`, `"Implementing: reading reference code..."`). The user should never see the same status message for more than ~90 seconds.

### 5. Commit

Report status: `tendril job status TendrilJobId --message "Committing changes..."`

Make logically grouped commits in the worktree(s). Each commit should be a coherent unit of work.

Before each commit, run formatting/linting as defined by the project's verifications. Fetch the full prompt for a verification with `tendril verification get <name>`.

**Example patterns** (actual commands come from verification prompts):

```bash
# Get changed files from this execution's commits
CHANGED_FILES=$(git diff --name-only --diff-filter=ACM HEAD~1)

# Run your formatter on changed files (examples):
# - .NET: dotnet format --include <files>
# - JavaScript: npm run format <files>
# - Python: black <files>
# - Go: gofmt -w <files>
```

If your formatter requires a workspace/solution file that isn't in the current directory, pass it as an explicit argument. Check `Memory/` for repo-specific workspace paths.

Commit messages should be clear and descriptive:

```
Add settings app with config display
```

After all commits, verify no uncommitted files remain:

```bash
git status
```

If there are uncommitted changes, either commit them or discard them with a clear reason. The worktree must be clean.

### 5.5. Generate Summary

Report status: `tendril job status TendrilJobId --message "Generating summary..."`

After all implementation commits are made, create `<TendrilPlanFolder>/Artifacts/summary.md` summarizing what was done.

The summary should follow this structure:

~~~markdown
# Summary

## Changes

<Brief description of what was implemented — 2-3 sentences max>

## API Changes

<List any new/changed/removed public APIs: classes, methods, properties, endpoints, CLI commands, config keys. Use code formatting. If no API changes, write "None.">

## Files Modified

<Bulleted list of key files changed, grouped by category. Don't list every file — focus on the important ones.>

## Manual Testing

<Step-by-step instructions for a human reviewer to verify this change works correctly. Include:
- What to launch/open (e.g., "Run the app", "Open the Plans view")
- What action to perform (e.g., "Click the Execute button on a plan with dependencies")
- What to observe (e.g., "The plan should show 'Blocked' status instead of executing")

If the change has no observable user-facing behavior (e.g., internal refactor, dependency update, code cleanup), write "N/A — internal change with no user-facing behavior.">
~~~

Focus on **what changed** (past tense), not what the plan said to do. Emphasize API surface changes — new classes, renamed methods, added properties, changed signatures — since these affect consumers.

Update the summary after verification fixes too — if verifications cause additional commits, append those changes to the summary.

### 6. Document Commits

Use the CLI to record commits, verifications, and related plans — **never edit plan.yaml directly**.

Add each commit hash:

```bash
tendril plan add-commit <plan-id> abc1234 --job-id TendrilJobId
tendril plan add-commit <plan-id> def5678 --job-id TendrilJobId
```

Set verification statuses from the plan revision. Set checked items (`- [x]`) to `Pending` and unchecked items (`- [ ]`) to `Skipped`:

```bash
tendril plan set-verification <plan-id> Build Pending --job-id TendrilJobId
tendril plan set-verification <plan-id> Test Skipped --job-id TendrilJobId
```

If the plan references other plans (e.g. split-from, follow-up), add them via CLI.

**CRITICAL:** The `tendril plan add-commit` and `tendril plan set-verification` CLI commands are the ONLY mechanism that updates plan.yaml. If you skip them, the plan will be marked as Failed even if all verifications pass. You MUST call these commands — do not assume writing verification report files is sufficient.

### 7. Run Verifications

Create a `Verification/` directory in the plan folder if it doesn't exist.

Check the `## Verification` section in the plan revision for checked items (`- [x]`). Skip unchecked items (`- [ ]`).

**Delegated verifications:** Some verifications are implemented as separate promptwares (e.g., `IvyFrameworkVerification`). The **Projects** section marks delegated verifications. Delegated verifications MUST be run via `tendril promptware run <Name>` — you are FORBIDDEN from writing their report files or setting their status to Pass yourself. If the `tendril` CLI is unavailable and you cannot invoke the sub-promptware, you MUST set the verification to `Fail` with a report explaining the CLI failure. Never self-certify a delegated verification.

**IMPORTANT — delegated invocation syntax:** The `tendril promptware run` CLI takes the plan folder as a **positional argument** (NOT a named flag like `--plan-folder`). You MUST also pass `--value` flags for each required firmware value. The exact command is in the verification's prompt (fetched via `tendril verification get <Name>`) — copy it character-for-character, only replacing angle-bracketed placeholders with actual paths. If the command is wrong, the child promptware receives no arguments and silently fails.

For each checked verification:

1. Send a status message: `tendril job status TendrilJobId --message "Verifying: <Name>"`
2. Fetch its full prompt: `tendril verification get <Name>`
3. **Check if delegated:** The **Projects** section indicates which verifications are delegated — follow the prompt's instructions to invoke it as an external process. If the external process cannot be invoked (CLI broken, file lock, etc.), set the verification to `Fail` immediately. Do NOT attempt to do the verification inline or write the report yourself.
4. Execute the prompt in the worktree directory
5. If it fails: diagnose, fix the issue, **commit the fix** (e.g. `Fix lint errors from Build`), and re-run. Repeat until it passes (fail the plan after 3+ failed attempts).
6. Document all fix commits via CLI: `tendril plan add-commit <plan-id> <sha> --job-id TendrilJobId`
7. Update the verification status via CLI: `tendril plan set-verification <plan-id> <Name> Pass --job-id TendrilJobId` (or `Fail`)

**CRITICAL:** You MUST call `tendril plan set-verification` after EACH verification. The verification report file alone is NOT sufficient — plan.yaml must also be updated via the CLI. Failing to call this command will result in the plan being marked as Failed.

**!IMPORTANT: Every verification MUST produce a report** at `<TendrilPlanFolder>/Verification/<VerificationName>.md` using YAML frontmatter:

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

The `result` field in the frontmatter MUST be one of: `Pass`, `Fail`, or `Skipped`. A verification is not complete without both its report file AND the `tendril plan set-verification` CLI call.

### 7.5. Generate Recommendations

Report status: `tendril job status TendrilJobId --message "Generating recommendations..."`

After all verifications pass, reflect on what you observed during this plan's execution. Write down anything you noticed that isn't part of this plan's scope:

- Follow-up work, edge cases not covered, or related features
- Confusing code, inconsistent patterns, or technical debt in the files you touched or read
- Unrelated bugs, broken tests, or incorrect behavior in surrounding code
- Performance improvements, unnecessary complexity, or refactoring opportunities

For each item, register it via the CLI:

```bash
tendril plan rec add <plan-id> "Short descriptive title" -d "Markdown description with context and location." --impact Medium --risk Small --job-id TendrilJobId
```

`--impact` and `--risk` are optional (Small, Medium, or High). Impact indicates the value of implementing it; Risk indicates the potential for complications or bugs.

Do NOT include items that are part of the current plan's scope. Do NOT include recommendations about code formatting, linting, or style issues — those are handled by verifications.

**After registering any recommendations via the CLI**, create `<TendrilPlanFolder>/Artifacts/recommendations.md`. Having zero recommendations is fine — but the file must still be created:

~~~markdown
# Recommendations

## Items

- **<Title>** — <one-line summary>
- **<Title>** — <one-line summary>

*Or: "None — <one sentence explaining why>"*
~~~

**This file is mandatory.** Step 8 will verify it exists and fail the plan if it is missing.

### 8. Final Clean Check

Report status: `tendril job status TendrilJobId --message "Running final checks..."`

After all verifications pass:

1. Kill any remaining processes spawned during plan execution (e.g. dev servers, sample apps). Find processes whose working directory or binary path is under the plan folder's artifacts directory and terminate them.

2. Clean up any temporary files created in Step 2.5 (e.g. generated config files, auth tokens).

3. Run `git status` in every worktree. If there are any uncommitted files (from verification fixes, generated files, etc.), commit or discard them. The worktrees must be completely clean before finishing.

4. Verify `<TendrilPlanFolder>/Artifacts/recommendations.md` exists. If missing, go back to Step 7.5.

5. Verify `<TendrilPlanFolder>/Artifacts/summary.md` exists. If missing, go back to Step 5.5.

### 8.5. Worktree Lifecycle

Worktrees are **not** cleaned up by ExecutePlan. They remain on disk so that CreatePr can push branches and create PRs directly from the worktree.

**Cleanup happens later, in two places:**
1. **CreatePr Step 5** — cleans up worktrees after PRs are created and (for yolo-rule repos) merged.
2. **WorktreeCleanupService** — safety net that runs every 30 minutes and removes worktrees for plans in terminal states (Completed, Failed, Skipped) after a 10-minute grace period.

**Git branches are preserved** until CreatePr consumes them — only the worktree filesystem directories are removed.

**Manual inspection:** If you need to inspect worktrees after failure, check the plan folder's `Worktrees/` directory before CreatePr runs. After PR creation, worktrees are cleaned up automatically. You can also temporarily pause WorktreeCleanupService if needed for extended debugging.

### 9. Plan State

**🚫 FORBIDDEN:** Do NOT call `tendril plan set <plan-id> state <anything>`. The Tendril server handles all state transitions automatically based on your exit code and verification statuses. Setting state manually causes the plan to appear in Review prematurely while your job is still running.

### Ambiguity Handling

You are running in non-interactive mode and CANNOT ask questions. If you are unsure about requirements, encounter conflicting instructions, or cannot find referenced files — STOP and fail with a clear message explaining what needs clarification. Do NOT guess when uncertain.

### Rules

- All work happens in worktree directories, never in the original repos
- Make logically grouped commits — not one giant commit
- Worktrees must be clean (no uncommitted files) when finished
- Document all commit hashes via `tendril plan add-commit` — never edit plan.yaml directly
- Follow the plan instructions exactly as written
- Do NOT skip tests or pre-commit formatting
- Commit messages must reference the plan ID
- Convert `file:///` paths in plans to local filesystem paths appropriate for your OS
- Do NOT commit artifact files (screenshots, images) to the repo. Test artifacts belong in `<TendrilPlanFolder>/Artifacts/` only — CreatePr handles uploading them to persistent storage.
- If the project uses private package registries, ensure authentication is configured before running dependency installation in worktrees. Credentials should come from environment variables or project-level configuration.
- Do NOT create filesystem aliases or shortcuts (e.g. symlinks, drive mappings) to worktree paths. The plans directory path is managed by Tendril — additional indirection causes cleanup issues.

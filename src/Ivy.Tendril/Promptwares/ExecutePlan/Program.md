# ExecutePlan

**Note:** This promptware is stack-agnostic. Stack-specific operations (build, format, test) are defined in `config.yaml` under `verifications`. Examples in this document use multiple tech stacks for illustration.

Execute an approved plan in isolated git worktrees.

## Context

The firmware header contains:

- **Args** / **PlanFolder** — path to the plan folder
- **CurrentTime** — current UTC timestamp
- **Note** (optional) — Additional instructions from the reviewer. If present, follow these instructions in addition to the plan.

The plan structure and CLI commands are in the **Reference Documents** section of your firmware.
Read the project configuration from `config.yaml` (referenced via `$TENDRIL_CONFIG` env var) for project repos and context.

The launcher sets the working directory to the project's primary repo.

**Note:** Plans are often executed multiple times. For example, a reviewer may not be satisfied with the first execution and sends the plan back to Draft with comments (via UpdatePlan). When re-executing, the worktree branch from the previous run may already exist — handle this gracefully (delete old worktree first, or create with a new branch suffix). Check for existing artifacts and verification reports from prior runs.

**Resume-vs-redo on re-execution:** Before deleting anything, run an integrity check on the prior run. If `plan.yaml` has commits populated and all verifications `Pass`, every `Pass` verification has a report, `artifacts/summary.md` exists, the worktree is clean with HEAD matching the last recorded commit, and the expected code changes are present in the files — then **resume** (log it and exit successfully) rather than redoing work. Redoing creates new commit hashes and breaks downstream CreatePr references. Only fall back to the full re-execution flow if any of those checks fail.

## Time Budget Awareness

**You have a 30-minute hard timeout.** Plan your time carefully:

1. **Spend at most 10 minutes reading/understanding the codebase**, then start implementing. If you haven't started writing code by the 10-minute mark, simplify your approach.

2. **Prefer implementing incrementally** (write code, build, fix errors) over exhaustive upfront research. You can read more code as needed during implementation.

3. **If the plan involves unfamiliar patterns, look at ONE good example and follow it** — don't survey every usage in the codebase. Find a single clear reference and proceed.

Focus on making progress, not achieving perfect understanding. A working implementation with minor imperfections beats a timeout with no code written.

## Execution Steps

### 1. Read Plan

- Read `plan.yaml` from the plan folder (project, repos, title)
- Read the latest revision from `revisions/` (highest numbered .md file)
- Extract the plan ID from the folder name (e.g. `01105` from `01105-TestPlan`)
- Report plan context to Jobs UI: `tendril job status $env:TENDRIL_JOB_ID --message "Reading plan..." --plan-id <plan-id> --plan-title "<title>"`

### 1.5. Verify Dependencies

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
   > ERROR: Repository at <repo-path> is itself a worktree. ExecutePlan cannot create worktrees inside worktrees. Update config.yaml to use the main repo path.

2. **Check Plans directory is not inside a worktree** — If `$TENDRIL_HOME` or its parent contains a worktree `.git` file, fail with error:
   > ERROR: TENDRIL_HOME is inside a git worktree. Move your Tendril installation or change the Plans directory.

```bash
# For each repo in plan.yaml repos (or project repos if empty):
cd <repo-path>

# Check if current directory is a worktree
if [ -f .git ] && grep -q "gitdir:" .git; then
    echo "ERROR: Repository at <repo-path> is itself a worktree."
    echo "ExecutePlan cannot create worktrees inside worktrees."
    echo "Check that config.yaml repo paths point to main repositories, not worktrees."
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

4. **Write validation report** — Create `<PlanFolder>/verification/PreExecution.md`:

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

   If any marker is found, verify the claim: run `gh pr view <cited PR> --json state,mergeCommit` (must be `MERGED`), confirm the cited commit is in `git log origin/<default-branch>`, and byte-compare the plan's proposed code against the current file contents. If all three checks pass, write `verification/PreExecution.md` with `Result: Fail`, write `artifacts/summary.md` documenting the no-op, set every verification to `Skipped` via `tendril plan set-verification <plan-id> <name> Skipped`, and fail the plan **without creating a worktree** — running verifications on unchanged code wastes the time budget and produces a 0-commit PR that CreatePr cannot process.

### 1.8. Auto-Commit Uncommitted Changes

Before creating worktrees, check each repo for uncommitted changes and automatically commit them. This prevents silent data loss when worktrees are created from `origin/<default-branch>` and later merged back.

For each repo listed in `plan.yaml` `repos` (or the project's repos from `config.yaml` if empty):

```bash
cd <repo-path>

if [[ -n $(git status --porcelain) ]]; then
  echo "Found uncommitted changes in $(pwd), checking for conflicts with recent commits..."
  
  STALE_FILES=()
  
  # Get list of dirty tracked files (modified/deleted/staged, not untracked)
  for file in $(git diff --name-only HEAD; git diff --cached --name-only) | sort -u; do
    # Check if this file was touched in last 5 commits
    RECENT_COMMIT=$(git log --oneline -1 -5 -- "$file" 2>/dev/null)
    if [[ -n "$RECENT_COMMIT" ]]; then
      COMMIT_HASH=$(echo "$RECENT_COMMIT" | awk '{print $1}')
      
      # Get the file content from before that commit
      PARENT_CONTENT=$(git show "${COMMIT_HASH}^:$file" 2>/dev/null)
      WORKING_CONTENT=$(cat "$file" 2>/dev/null)
      
      if [[ "$PARENT_CONTENT" == "$WORKING_CONTENT" ]]; then
        echo "WARNING: Stale file '$file' matches pre-commit state of $RECENT_COMMIT"
        echo "  Discarding stale version — keeping committed (HEAD) version."
        STALE_FILES+=("$file")
      fi
    fi
  done
  
  # Auto-resolve: discard stale files by restoring HEAD versions
  if [[ ${#STALE_FILES[@]} -gt 0 ]]; then
    echo "Auto-resolving ${#STALE_FILES[@]} stale file(s)..."
    for stale in "${STALE_FILES[@]}"; do
      git checkout HEAD -- "$stale"
      echo "  Restored: $stale"
    done
  fi
  
  # After resolving stale files, check if there are still changes to commit
  if [[ -n $(git status --porcelain) ]]; then
    git add -A
    git reset -- '*.bak_*' 2>/dev/null || true
    git commit -m "WIP: Auto-commit before plan execution [$(date -u +%Y-%m-%dT%H:%M:%SZ)]"
    git push origin $(git branch --show-current)
    echo "Changes committed and pushed successfully"
  else
    echo "All dirty files were stale — nothing to commit after cleanup."
  fi
fi
```

**Rationale:**
- Worktrees branch from `origin/<default-branch>` (Step 2), so unpushed local changes won't be in the worktree base
- When the PR merges and CreatePr pulls main back, `git pull` would overwrite any uncommitted local changes
- Auto-committing and pushing ensures all local work is preserved and visible to worktrees
- The `WIP:` prefix makes auto-commits easily identifiable for later cleanup (squash/amend)
- **Revert detection with auto-resolve:** Before committing, each dirty tracked file — whether unstaged (`git diff --name-only HEAD`) or staged (`git diff --cached --name-only`) — is checked against the last 5 commits. If the working tree version matches the file's state *before* a recent commit (i.e., it's stale), the file is automatically restored to its HEAD version via `git checkout HEAD -- <file>`. This prevents silent reverts while keeping the process fully autonomous. Any remaining non-stale dirty files are committed normally.
- **Backup file exclusion:** After staging all changes with `git add -A`, the command `git reset -- '*.bak_*'` explicitly unstages any files matching the backup pattern. This prevents temporary backup files (created by prior plan executions as local recovery points) from being committed to version control. Backup files serve only as local recovery points and should not pollute the repository history.

**Note:** This step runs in the original repo directories, before worktree creation.

### 2. Create Worktrees

For each repo in `RepoConfigs` (this includes both the plan's repos AND any read-only build dependencies from the project config):

1. Fetch latest from remote: `git fetch origin`
2. Determine the base branch:
   - Check the `RepoConfigs` firmware header for this repo's `baseBranch` value
   - If configured, use that value as the base branch
   - Otherwise, auto-detect via: `git symbolic-ref refs/remotes/origin/HEAD | sed 's|refs/remotes/origin/||'`
3. If the worktree or branch already exists from a prior execution, remove it first. A prior run may have left a **stale directory** (the filesystem tree still exists but git no longer tracks it as a worktree — there's no `.git` file at the worktree root). In that case `git worktree remove` will fail with "is not a working tree"; you must also `rm -rf` the directory. **Do all three unconditionally** so the next `git worktree add` starts from a clean slate:

```bash
PLAN_FOLDER_NAME=$(basename "<PlanFolder>")
PLAN_ID=$(echo "$PLAN_FOLDER_NAME" | grep -oP '^\d+')
SAFE_TITLE=$(echo "$PLAN_FOLDER_NAME" | sed 's/^[0-9]\+-//')
BRANCH_NAME="tendril/$PLAN_ID-$SAFE_TITLE"

git worktree remove "<PlanFolder>/worktrees/<repo-folder-name>" --force 2>/dev/null
git branch -D "$BRANCH_NAME" 2>/dev/null
rm -rf "<PlanFolder>/worktrees/<repo-folder-name>"
```

**Note on stale directories:** If a stale worktree directory exists and you run `git -C <stale-dir> status`, git silently walks up the parent chain and reports the state of the main repo — making it look like the "worktree" is simply on `main`. Do not trust that output. Before assuming a prior worktree is intact, verify with `git -C <main-repo> worktree list | grep <path>` or check that `<worktree-path>/.git` exists.

1. Create worktree branching from the remote default branch:

```bash
cd <original-repo-path>
git fetch origin
PLAN_FOLDER_NAME=$(basename "<PlanFolder>")
PLAN_ID=$(echo "$PLAN_FOLDER_NAME" | grep -oP '^\d+')
SAFE_TITLE=$(echo "$PLAN_FOLDER_NAME" | sed 's/^[0-9]\+-//')
BRANCH_NAME="tendril/$PLAN_ID-$SAFE_TITLE"
git worktree add "<PlanFolder>/worktrees/<repo-folder-name>" -b "$BRANCH_NAME" "origin/<resolved-base-branch>"
```

Example:

```bash
cd <RepoPath>
git fetch origin
git worktree add "<PlanFolder>/worktrees/<RepoName>" -b "tendril/<PlanId>-<SafeTitle>" origin/<resolved-base-branch>
```

**Important:** Always branch from `origin/<resolved-base-branch>`, not local HEAD. This ensures the PR only contains the plan's commits, not any unpushed local work. The `<resolved-base-branch>` comes from either the `RepoConfigs` firmware header (if `baseBranch` is configured) or auto-detection.

**Note on `RepoConfigs`:** The firmware header may include a `RepoConfigs` value injected by Tendril. It contains per-repo configuration from `config.yaml`:
```yaml
RepoConfigs: |
  - path: /home/user/repos/my-project
    baseBranch: main
    syncStrategy: fetch
    prRule: yolo
  - path: /home/user/repos/shared-lib
    baseBranch: main
    syncStrategy: fetch
    prRule: default
    readOnly: true
```
If `baseBranch` is present for a repo, use it instead of auto-detecting. If absent, fall back to `git symbolic-ref refs/remotes/origin/HEAD`.

**Read-only repos** (`readOnly: true`) are build dependencies — they need worktrees so that cross-repo project references resolve, but you must NOT make changes, commits, or PRs in them. Create their worktrees the same way (branching from `origin/<baseBranch>`), but skip them during implementation steps 3-5.

4. After creating the worktree, **verify the `.git` file exists** and fail fast if it's missing:

```bash
if [ ! -f "<PlanFolder>/worktrees/<repo-folder-name>/.git" ]; then
    echo "ERROR: Worktree creation failed - .git file missing at <PlanFolder>/worktrees/<repo-folder-name>/.git"
    echo "This indicates git worktree add did not fully initialize the worktree."
    exit 1
fi
cat "<PlanFolder>/worktrees/<repo-folder-name>/.git"
```

This ensures ExecutePlan fails immediately if worktree creation is incomplete, rather than leaving orphaned directories that trigger warnings during cleanup.

5. **Apply sync strategy** — REQUIRED after worktree creation and `.git` file verification:

   ```bash
   SYNC_STRATEGY="<from RepoConfigs or 'fetch' if not specified>"
   BASE_BRANCH="<resolved-base-branch>"
   WORKTREE_PATH="<PlanFolder>/worktrees/<repo-folder-name>"

   # Invoke the Apply-SyncStrategy tool
   Tools/Apply-SyncStrategy -WorktreePath "$WORKTREE_PATH" -SyncStrategy "$SYNC_STRATEGY" -BaseBranch "$BASE_BRANCH"
   ```

   This tool applies the configured sync strategy (fetch/rebase/merge) to keep the worktree branch synchronized with the base branch. It handles errors and logs each step.

   **When to call:** After each worktree is created (Step 2.4) and before moving to the next repo.

   **Note:** For `syncStrategy: "rebase"` or `syncStrategy: "merge"`, this operation should also be performed before making commits during plan execution to keep the branch up-to-date with upstream changes. Use the same tool with the same parameters.

   **Error handling:**
   - If `Apply-SyncStrategy` fails (non-zero exit code), the entire ExecutePlan run should fail
   - Common failure scenarios:
     - `git fetch` fails → network issue or invalid remote
     - `git rebase` fails → conflicting changes between worktree base and origin
     - `git merge` fails → conflicting changes or uncommitted files
   - On failure, log the error and exit. Do NOT attempt to continue with an out-of-sync worktree.

### 2.5. Setup Build Dependencies in Worktrees

**Note:** This section applies only when the project has build-time dependencies (e.g. frontend packages, generated code, pre-built artifacts) that need special handling in worktrees. Skip if not applicable.

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

Work exclusively in the worktree directories. Follow the plan's latest revision:

1. **Problem** — Understand what needs to be done
2. **Solution** — Execute the implementation steps in the worktree
3. **Tests** — Write and run all tests specified in the plan

### 5. Commit

Make logically grouped commits in the worktree(s). Each commit should be a coherent unit of work.

Before each commit, run formatting/linting as defined by the project's verifications in `config.yaml`. The exact commands depend on your stack's verification definitions.

**Example patterns** (actual commands come from config.yaml verifications):

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

Commit messages should reference the plan ID:

```
[01105] Add settings app with config display
```

After all commits, verify no uncommitted files remain:

```bash
git status
```

If there are uncommitted changes, either commit them or discard them with a clear reason. The worktree must be clean.

### 5.5. Generate Summary

After all implementation commits are made, create `<PlanFolder>/artifacts/summary.md` summarizing what was done.

The summary should follow this structure:

~~~markdown
# Summary

## Changes

<Brief description of what was implemented — 2-3 sentences max>

## API Changes

<List any new/changed/removed public APIs: classes, methods, properties, endpoints, CLI commands, config keys. Use code formatting. If no API changes, write "None.">

## Files Modified

<Bulleted list of key files changed, grouped by category. Don't list every file — focus on the important ones.>
~~~

Focus on **what changed** (past tense), not what the plan said to do. Emphasize API surface changes — new classes, renamed methods, added properties, changed signatures — since these affect consumers.

Update the summary after verification fixes too — if verifications cause additional commits, append those changes to the summary.

### 6. Document Commits

Use the CLI to record commits, verifications, and related plans — **never edit plan.yaml directly**.

Add each commit hash:

```bash
tendril plan add-commit <plan-id> abc1234
tendril plan add-commit <plan-id> def5678
```

Set verification statuses from the plan revision. Set checked items (`- [x]`) to `Pending` and unchecked items (`- [ ]`) to `Skipped`:

```bash
tendril plan set-verification <plan-id> Build Pending
tendril plan set-verification <plan-id> Test Skipped
```

If the plan references other plans (e.g. split-from, follow-up), add them via CLI.

**CRITICAL:** The `tendril plan add-commit` and `tendril plan set-verification` CLI commands are the ONLY mechanism that updates plan.yaml. If you skip them, the plan will be marked as Failed even if all verifications pass. You MUST call these commands — do not assume writing verification report files is sufficient.

### 7. Run Verifications

Create a `verification/` directory in the plan folder if it doesn't exist.

Check the `## Verification` section in the plan revision for checked items (`- [x]`). Skip unchecked items (`- [ ]`).

**Delegated verifications:** Some verifications are implemented as separate promptwares (e.g., `IvyFrameworkVerification`). A verification is **delegated** if its name matches an entry in the `promptwares` section of `config.yaml`. Delegated verifications MUST be run via `tendril promptware <Name>` — you are FORBIDDEN from writing their report files or setting their status to Pass yourself. If the `tendril` CLI is unavailable and you cannot invoke the sub-promptware, you MUST set the verification to `Fail` with a report explaining the CLI failure. Never self-certify a delegated verification.

For each checked verification:

1. Send a status message: `tendril job status $env:TENDRIL_JOB_ID --message "Verifying: <Name>"`
2. Look up its `prompt` in the `verifications` list in `config.yaml`
3. **Check if delegated:** If the verification name exists in config.yaml's `promptwares` section, it is a delegated verification — follow the prompt's instructions to invoke it as an external process. If the external process cannot be invoked (CLI broken, file lock, etc.), set the verification to `Fail` immediately. Do NOT attempt to do the verification inline or write the report yourself.
4. Execute the prompt in the worktree directory
5. If it fails: diagnose, fix the issue, **commit the fix** (e.g. `[01105] Fix lint errors from Build`), and re-run. Repeat until it passes (fail the plan after 3+ failed attempts).
6. Document all fix commits via CLI: `tendril plan add-commit <plan-id> <sha>`
7. Update the verification status via CLI: `tendril plan set-verification <plan-id> <Name> Pass` (or `Fail`)

**CRITICAL:** You MUST call `tendril plan set-verification` after EACH verification. The verification report file alone is NOT sufficient — plan.yaml must also be updated via the CLI. Failing to call this command will result in the plan being marked as Failed.

**!IMPORTANT: Every verification MUST produce a report** at `<PlanFolder>/verification/<VerificationName>.md` using YAML frontmatter:

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

After all verifications pass, reflect on what you observed during this plan's execution. Write down anything you noticed that isn't part of this plan's scope:

- Follow-up work, edge cases not covered, or related features
- Confusing code, inconsistent patterns, or technical debt in the files you touched or read
- Unrelated bugs, broken tests, or incorrect behavior in surrounding code
- Performance improvements, unnecessary complexity, or refactoring opportunities

For each item, register it via the CLI:

```bash
tendril plan rec add <plan-id> "Short descriptive title" -d "Markdown description with context and location." --impact Medium --risk Small
```

`--impact` and `--risk` are optional (Small, Medium, or High). Impact indicates the value of implementing it; Risk indicates the potential for complications or bugs.

Do NOT include items that are part of the current plan's scope. Do NOT include recommendations about code formatting, linting, or style issues — those are handled by verifications.

**After registering any recommendations via the CLI**, create `<PlanFolder>/artifacts/recommendations.md`. Having zero recommendations is fine — but the file must still be created:

~~~markdown
# Recommendations

## Items

- **<Title>** — <one-line summary>
- **<Title>** — <one-line summary>

*Or: "None — <one sentence explaining why>"*
~~~

**This file is mandatory.** Step 8 will verify it exists and fail the plan if it is missing.

### 8. Final Clean Check

After all verifications pass:

1. Kill any remaining processes spawned during plan execution (e.g. dev servers, sample apps). Find processes whose working directory or binary path is under the plan folder's artifacts directory and terminate them.

2. Clean up any temporary files created in Step 2.5 (e.g. generated config files, auth tokens).

3. Run `git status` in every worktree. If there are any uncommitted files (from verification fixes, generated files, etc.), commit or discard them. The worktrees must be completely clean before finishing.

4. Verify `<PlanFolder>/artifacts/recommendations.md` exists. If missing, the plan **must fail** — go back to Step 7.5.

### 8.5. Worktree Lifecycle

Worktrees are **not** cleaned up by ExecutePlan. They remain on disk so that CreatePr can push branches and create PRs directly from the worktree.

**Cleanup happens later, in two places:**
1. **CreatePr Step 5** — cleans up worktrees after PRs are created and (for yolo-rule repos) merged.
2. **WorktreeCleanupService** — safety net that runs every 30 minutes and removes worktrees for plans in terminal states (Completed, Failed, Skipped) after a 10-minute grace period.

**Git branches are preserved** until CreatePr consumes them — only the worktree filesystem directories are removed.

**Manual inspection:** If you need to inspect worktrees after failure, check the plan folder's `worktrees/` directory before CreatePr runs. After PR creation, worktrees are cleaned up automatically. You can also temporarily pause WorktreeCleanupService if needed for extended debugging.

### 9. Plan State

The launcher script handles state transitions (Completed/Failed) based on exit code.

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
- Do NOT commit artifact files (screenshots, images) to the repo. Test artifacts belong in `<PlanFolder>/artifacts/` only — CreatePr handles uploading them to persistent storage.
- If the project uses private package registries, ensure authentication is configured before running dependency installation in worktrees. Credentials should come from environment variables or project-level configuration.
- Do NOT create filesystem aliases or shortcuts (e.g. symlinks, drive mappings) to worktree paths. The plans directory path is managed by Tendril — additional indirection causes cleanup issues.

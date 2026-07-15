# SyncRepo

Get a local repository to a clean, up-to-date state on the expected base branch without losing any user work.

## Context

The firmware header contains:
- **RepoPath** — absolute path to the repository
- **BaseBranch** — the branch the repo should be on (e.g. "main", "development")
- **UntrackedChangesPolicy** — how to handle uncommitted changes and untracked files: `Stash`, `Commit`, or `PullRequest` (see step 6)
- **TendrilJobId** — for status reporting

## Rules

- **Never discard work.** Local work is preserved according to **UntrackedChangesPolicy** (stashed, committed, or turned into a pull request) — never `reset`. Unpushed commits are pushed, not dropped. Detached HEAD commits get a rescue branch.
- **Fetch before you decide.** Refresh `origin/BaseBranch` (step 5) before comparing ahead/behind or pushing, so every decision is made against the real remote state, not a stale local ref.
- **Fail explicitly.** If a step cannot be resolved automatically (e.g. rebase conflicts, diverged state, a protected base branch that rejects the push), report the issue clearly and stop. Do not leave the repo in a partially-synced state.
- **A missing remote is not a failure.** If the repo has no remote, apply the local-changes policy (commit/stash still happen) and skip the network steps — that is "as synced as possible" for a repo with nowhere to push.
- **Report all actions.** Log what was done (stashed, pushed, switched branch, etc.) so the user has a trail.
- **No submodule recursion** for now — skip if submodules are present.

## Execution Steps

### 1. Report Status

```bash
tendril job status TendrilJobId --message="Syncing repo: BaseBranch..."
```

### 2. Abort In-Progress Operations

Check if the repo is mid-rebase, mid-merge, mid-cherry-pick, or mid-bisect.

Look for these indicators:
- `.git/MERGE_HEAD` → `git merge --abort`
- `.git/rebase-merge/` or `.git/rebase-apply/` → `git rebase --abort`
- `.git/CHERRY_PICK_HEAD` → `git cherry-pick --abort`
- `.git/BISECT_LOG` → `git bisect reset`

Abort whichever is found. Log: "Aborted in-progress {operation}."

### 3. Handle Detached HEAD

Check if HEAD is detached:
```bash
git symbolic-ref HEAD
```
If exit code != 0, HEAD is detached.

Before leaving detached state, check if there are commits not reachable from any branch:
```bash
git log HEAD --not --branches --oneline
```
If output is non-empty, create a rescue branch:
```bash
git branch "rescue/$(git rev-parse --short HEAD)"
```
Log: "Created rescue branch rescue/{sha} to preserve detached commits."

Then checkout the expected base branch:
```bash
git checkout BaseBranch
```

### 4. Switch to Expected Base Branch

Check current branch:
```bash
git symbolic-ref --short HEAD
```

If not on BaseBranch:
1. If there are uncommitted changes on the current branch, stash them first so the branch switch is clean. Use a representative message of the form `SyncRepo: <representative description of content>` (these are the previous branch's changes — describe them, don't use a generic label):
   ```bash
   git stash push --include-untracked -m "SyncRepo: <representative description of content>"
   ```
2. Switch:
   ```bash
   git checkout BaseBranch
   ```

Log: "Switched from {old-branch} to BaseBranch."

> The **UntrackedChangesPolicy** in step 6 applies to local changes present on **BaseBranch**. Changes stashed here to leave a different branch are always preserved as a stash and surfaced in step 9.

### 5. Fetch from Origin

Refresh the remote-tracking ref **before** any ahead/behind decision so later steps compare against the real remote, not a stale local ref.

First check a remote exists:
```bash
git remote
```

If the output is empty, there is **no remote**. Log: "No remote configured — committing/stashing locally; skipping push and origin sync." Then run step 6 (apply the policy) and **skip steps 7 and 8** (push and pull); step 10 verifies local state only (do not check "not ahead of origin").

Otherwise fetch:
```bash
git fetch origin
```
This guarantees `origin/BaseBranch` exists locally and is up to date for the comparisons in steps 7–8 and the final verification. Log: "Fetched origin."

### 6. Handle Local Changes (per UntrackedChangesPolicy)

First detect what local work exists:
```bash
git status --porcelain          # tracked changes: lines NOT starting with ??
git ls-files --others --exclude-standard   # untracked files (not ignored)
```

If there are **no** uncommitted changes and **no** untracked files, skip this step entirely.

Otherwise branch on **UntrackedChangesPolicy** from the firmware header:

#### Policy: `Stash`

Stash tracked changes and untracked files together so nothing is lost. The stash message MUST follow the form `SyncRepo: <representative description of content>` — inspect the changed files and write a short human description of what the changes are about (e.g. `SyncRepo: config.yaml project reordering`, `SyncRepo: WIP auth refactor + scratch notes`). Do NOT use a generic message like "uncommitted changes".

```bash
git stash push --include-untracked -m "SyncRepo: <representative description of content>"
```
Log: "Stashed local changes: {message}."

#### Policy: `Commit`

Commit the local work onto **BaseBranch** and let the push in step 7 deliver it to origin.

1. Stage untracked files as needed (`git add <paths>`), then group all changes into **logical commits** — one commit per coherent unit of work, not one giant commit. Inspect the diff and cluster by feature/area/intent.
2. Use clear, conventional, descriptive commit messages that summarize each group (e.g. `Reorder projects in TeamIvy config`, `Add --open flag to dev review action`). Never use placeholder messages like "wip" or "changes".
3. If changes are trivial or all clearly one unit, a single well-named commit is fine.

```bash
git add <paths for group 1>
git commit -m "<good message for group 1>"
git add <paths for group 2>
git commit -m "<good message for group 2>"
# ...one commit per logical group
```
Log: "Committed local changes in {n} logical commit(s)." The commits are pushed to `origin/BaseBranch` in step 7.

> Note: this policy commits and pushes changes that are present on **BaseBranch**. Any changes that were stashed in step 4 to leave a *different* branch belong to that other branch and remain a stash — they are **not** committed here. If that happened, log it prominently: "Note: changes from {old-branch} were preserved as a stash, not committed to BaseBranch (see step 9)."

#### Policy: `PullRequest`

Put the local work on a dedicated branch and open a pull request instead of pushing to **BaseBranch**.

1. Create and switch to a new branch off the current **BaseBranch**, e.g. `git checkout -b syncrepo/<short-topic>` (name it after what the changes are about).
2. Group the changes into **logical commits** with good messages, exactly as in the `Commit` policy above.
3. Push the branch and open a PR targeting **BaseBranch**:
   ```bash
   git push -u origin syncrepo/<short-topic>
   gh pr create --base BaseBranch --head syncrepo/<short-topic> --title "<descriptive title>" --body "<summary of the changes>"
   ```
4. If `gh` is unavailable or PR creation fails, FAIL clearly: "Could not open pull request; branch syncrepo/<short-topic> pushed with your changes." (the work is safe on the pushed branch).
5. **Switch back to BaseBranch** so the remaining sync steps run on the right branch (your work is safe on the pushed branch / PR):
   ```bash
   git checkout BaseBranch
   ```

Log: "Opened pull request from syncrepo/<short-topic> into BaseBranch."

> Note: after the `PullRequest` policy the working tree is clean, HEAD is back on **BaseBranch**, and BaseBranch carries no new local commits, so steps 7–8 simply fast-forward BaseBranch to origin. (If you skip the checkout back to BaseBranch, step 10's "Confirm on BaseBranch" will fail.)

### 7. Push Local Commits

Skip this step entirely if there is **no remote** (step 5).

Check whether HEAD has local commits to deliver (origin/BaseBranch is fresh from step 5's fetch):
```bash
git rev-list origin/BaseBranch..HEAD --count
```

If the count is 0, there is nothing to push — skip to step 8.

If the count > 0, **integrate any divergence first, then push** (fetching first means the push is fast-forward on the first try in the common case). If origin has also moved (HEAD is behind or diverged), rebase local commits on top before pushing:
```bash
git pull --rebase origin BaseBranch     # only needed if origin advanced; conflicts -> FAIL
git push origin BaseBranch
```
If the rebase has conflicts → FAIL: "Repo has diverged from origin and has conflicts. Manual intervention required."

**Handle push rejections by cause** — inspect the push output:

- **Non-fast-forward / "fetch first"** (a concurrent push landed while we were working): re-integrate and retry, in a **bounded loop of at most 3 attempts**:
  ```bash
  git fetch origin
  git pull --rebase origin BaseBranch   # conflicts -> FAIL (as above)
  git push origin BaseBranch
  ```
  If still rejected after 3 attempts → FAIL: "Could not push to origin/BaseBranch after repeated retries (origin keeps advancing). Manual intervention required."

- **Protected branch / permission denied** — the push output contains any of `GH006`, `protected branch`, `pre-receive hook declined`, `403`, or `permission denied`. Do **NOT** retry or rebase (retrying cannot help). FAIL clearly: "Commit(s) created locally but BaseBranch is protected — direct push is blocked. Use the PullRequest policy to deliver these changes." The commits are safe locally; origin is intentionally left un-synced rather than force-pushed.

- **Any other push error** → FAIL and include the git error output verbatim.

Log: "Pushed {n} local commits to origin."

### 8. Pull Latest from Origin

Skip this step if there is **no remote** (step 5).

```bash
git fetch origin
git merge --ff-only origin/BaseBranch
```

If fast-forward fails → FAIL: "Cannot fast-forward BaseBranch to match origin. Manual intervention required."

Log: "Updated to latest origin/BaseBranch."

### 9. Report Stashes

```bash
git stash list
```

If stashes exist, log a warning listing them:
"WARNING: Repo has {n} stash(es). Review and drop when no longer needed."

### 10. Final Verification

If there **is** a remote, fetch once more so the "not ahead of origin" check reflects what actually landed on the remote (not just the local ref):
```bash
git fetch origin
```

Run the same checks as IsDirtyRepo:
- Confirm on BaseBranch
- Confirm not ahead of origin (skip this check if there is no remote)
- Confirm no uncommitted changes
- Confirm no in-progress operations

If still dirty → FAIL with the remaining issues.

Log: "Repo synced successfully: on BaseBranch, up to date with origin."

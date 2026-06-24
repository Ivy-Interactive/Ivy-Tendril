# SyncRepo

Get a local repository to a clean, up-to-date state on the expected base branch without losing any user work.

## Context

The firmware header contains:
- **RepoPath** — absolute path to the repository
- **BaseBranch** — the branch the repo should be on (e.g. "main", "development")
- **TendrilJobId** — for status reporting

## Rules

- **Never discard work.** Uncommitted changes are stashed, not reset. Unpushed commits are pushed, not dropped. Detached HEAD commits get a rescue branch.
- **Fail explicitly.** If a step cannot be resolved automatically (e.g. rebase conflicts, diverged state), report the issue clearly and stop. Do not leave the repo in a partially-synced state.
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
1. If there are uncommitted changes, stash them first:
   ```bash
   git stash push -m "SyncRepo: auto-stash before branch switch"
   ```
2. Switch:
   ```bash
   git checkout BaseBranch
   ```

Log: "Switched from {old-branch} to BaseBranch."

### 5. Stash Uncommitted Changes

Check for staged or unstaged changes to tracked files:
```bash
git status --porcelain
```

If output is non-empty (excluding lines starting with `??`):
```bash
git stash push -m "SyncRepo: uncommitted changes"
```
Log: "Stashed uncommitted changes ({n} files)."

### 6. Stash Untracked Files

Check for untracked files (not ignored):
```bash
git ls-files --others --exclude-standard
```

If output is non-empty:
```bash
git stash push --include-untracked -m "SyncRepo: untracked files"
```
Log: "Stashed {n} untracked files."

### 7. Push Local Commits

Check if ahead of origin:
```bash
git rev-list origin/BaseBranch..HEAD --count
```

If count > 0:
```bash
git push origin BaseBranch
```

If push is rejected (non-fast-forward):
```bash
git pull --rebase origin BaseBranch
```
If rebase has conflicts → FAIL: "Repo has diverged from origin and has conflicts. Manual intervention required."

If rebase succeeds:
```bash
git push origin BaseBranch
```

Log: "Pushed {n} local commits to origin."

### 8. Pull Latest from Origin

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

Run the same checks as IsDirtyRepo:
- Confirm on BaseBranch
- Confirm not ahead of origin
- Confirm no uncommitted changes
- Confirm no in-progress operations

If still dirty → FAIL with the remaining issues.

Log: "Repo synced successfully: on BaseBranch, up to date with origin."

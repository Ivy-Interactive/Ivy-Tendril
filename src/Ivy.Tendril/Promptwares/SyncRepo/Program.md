# SyncRepo

Get a local repository to a clean, up-to-date state on the expected base branch without losing any user work.

## Context

The firmware header contains:
- **RepoPath** — absolute path to the repository
- **BaseBranch** — the branch the repo should be on (e.g. "main", "development")
- **UntrackedChangesPolicy** — how to handle uncommitted changes and untracked files: `Stash`, `Commit`, or `PullRequest` (see step 5)
- **TendrilJobId** — for status reporting

## Rules

- **Never discard work.** Local work is preserved according to **UntrackedChangesPolicy** (stashed, committed, or turned into a pull request) — never `reset`. Unpushed commits are pushed, not dropped. Detached HEAD commits get a rescue branch.
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
1. If there are uncommitted changes on the current branch, stash them first so the branch switch is clean. Use a representative message of the form `SyncRepo: <representative description of content>` (these are the previous branch's changes — describe them, don't use a generic label):
   ```bash
   git stash push --include-untracked -m "SyncRepo: <representative description of content>"
   ```
2. Switch:
   ```bash
   git checkout BaseBranch
   ```

Log: "Switched from {old-branch} to BaseBranch."

> The **UntrackedChangesPolicy** in step 5 applies to local changes present on **BaseBranch**. Changes stashed here to leave a different branch are always preserved as a stash and surfaced in step 8.

### 5. Handle Local Changes (per UntrackedChangesPolicy)

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

Commit the local work onto **BaseBranch** and let the push in step 6 deliver it to origin.

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
Log: "Committed local changes in {n} logical commit(s)." The commits are pushed to `origin/BaseBranch` in step 6.

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

> Note: after the `PullRequest` policy the working tree is clean, HEAD is back on **BaseBranch**, and BaseBranch carries no new local commits, so steps 6–7 simply fast-forward BaseBranch to origin. (If you skip the checkout back to BaseBranch, step 9's "Confirm on BaseBranch" will fail.)

### 6. Push Local Commits

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

### 7. Pull Latest from Origin

```bash
git fetch origin
git merge --ff-only origin/BaseBranch
```

If fast-forward fails → FAIL: "Cannot fast-forward BaseBranch to match origin. Manual intervention required."

Log: "Updated to latest origin/BaseBranch."

### 8. Report Stashes

```bash
git stash list
```

If stashes exist, log a warning listing them:
"WARNING: Repo has {n} stash(es). Review and drop when no longer needed."

### 9. Final Verification

Run the same checks as IsDirtyRepo:
- Confirm on BaseBranch
- Confirm not ahead of origin
- Confirm no uncommitted changes
- Confirm no in-progress operations

If still dirty → FAIL with the remaining issues.

Log: "Repo synced successfully: on BaseBranch, up to date with origin."

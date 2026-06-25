# CreatePr

**Note:** This promptware is stack-agnostic. Stack-specific operations (build, format, test) are defined as verifications in the project configuration. Examples in this document use multiple tech stacks for illustration.

Create GitHub pull requests, then merge them only when explicitly told to.

**!CRITICAL: ALL steps are mandatory. Whether to merge is controlled entirely by the `PrMerge` firmware header value — never infer it from anything else.**

## Context

The firmware header contains:
- **TendrilPlanFolder** — path to the plan folder
- **CurrentTime** — current UTC timestamp
- **SourceUrl** — (optional) GitHub issue or PR URL from plan.yaml
- **`Pr*` flags** — the explicit PR options (see step 1). These are the *only* source of truth for what to do.

The plan structure and CLI commands are in the **Reference Documents** section of your firmware.

## Execution Steps

> **Transient-error retry convention (applies to every `git` and `gh` command below):**
> Network/remote operations intermittently fail and then succeed on a second try. Whenever a
> `git push`/`git fetch`/`git pull` or any `gh` command fails with a **transient** error,
> **retry up to 3 times with exponential backoff (~2s, then 4s, then 8s)** before treating it
> as a real failure. Treat these as transient (case-insensitive):
> `could not resolve host`, `connection reset`, `connection timed out`, `could not connect`,
> `failed to connect`, `kex_exchange_identification`, `early EOF`, `RPC failed`, `TLS`,
> HTTP `5xx`, `429`, `rate limit exceeded` (GitHub API), `Bad gateway`, `timed out`.
> Do **not** retry genuine, non-transient errors — authentication failures, `not found` /
> invalid repo, permission/`403`, validation errors, or merge conflicts — fail fast on those
> with a clear, specific message.

### 0. Check Plan State

Before processing, read `plan.yaml` and check the `state` field. After reading, report plan context: `tendril job status TendrilJobId --message="Creating PR..." --plan-id=<plan-id> --plan-title="<title>"`
- If `state: Completed`, the plan was already processed. Exit early with a message indicating the plan is already completed and showing the existing PR URLs from the `prs` list.
- Otherwise, proceed with step 1.

### 1. Read Plan

- Read `plan.yaml` from the plan folder (project, commits, repos)
- Read the latest revision for the plan title and description
- **Read the PR option flags** from the firmware header. These drive every decision below. If a flag is absent, use the default shown:
  - `PrSolveMergeConflicts` — `true`/`false` (default: `true`)
  - `PrMerge` — `true`/`false` (default: `true`) — **whether to merge the PR after creating it**
  - `PrDeleteBranch` — `true`/`false` (default: `true`)
  - `PrIncludeArtifacts` — `true`/`false` (default: `true`)
  - `PrReviewer` — GitHub username to request as reviewer (default: none)
  - `PrComment` — Review comment text (default: none)
  - `PrDraft` — `true`/`false` (default: `false`)

### 2. For Each Worktree

Report status: `tendril job status TendrilJobId --message="Pushing branches..."`

Check `<TendrilPlanFolder>/Worktrees/` for each repo worktree.

> **Worktree already removed:** If the Worktrees/ directory is empty (worktree was already cleaned up), fall back to `plan.yaml` to get the repo path and branch name (format: `tendril/<planId>-<SafeTitle>`, where SafeTitle is extracted from the plan folder name: e.g. `03158-ChangeBranchNaming` → `ChangeBranchNaming`). The commit objects may still exist in the original repo's object store. Use `git cat-file -t <sha>` to verify, then create or force-update the local branch: `git branch -f <branch-name> <sha>` (use `-f` because the branch may already exist from a WIP auto-commit) and push from the original repo path.
>
> **Commit lost (object GC'd):** If `git cat-file -t <sha>` fails, the commit was garbage-collected after worktree removal. In this case: (1) check if the change is already on main, (2) if not, recreate the change from the plan revision — create a new branch from main, apply the changes as described in the revision, commit with a descriptive message matching the plan title, and push. Update commits via CLI: `tendril plan add-commit <plan-id> <new-sha>`.

For each worktree:

1. `git remote get-url origin` (from the worktree) to get the GitHub remote
2. Extract `owner/repo` from the remote URL
3. **!MANDATORY project gate — do not skip.** Verify this worktree's repo is one the project
   authorizes: its repo path/name must appear in the `RepoConfigs` firmware header (the project's
   configured repos). If the worktree's repo is **NOT** in `RepoConfigs`, **abort this worktree**:
   do **not** push, create a PR, or merge. Report the mismatch and fail the job for this repo:
   ```bash
   tendril job status TendrilJobId --message="ERROR: worktree repo <owner/repo> is not part of this project's RepoConfigs — refusing to push/merge. The plan was likely created in the wrong project."
   ```
   Then skip to the next worktree (or exit if this is the only one). This is the stop that prevents
   merging a change into a repo outside the plan's project (#1340). Never push to a repo just because
   a worktree for it exists on disk.
4. `git rev-parse --abbrev-ref HEAD` to get the branch name
5. `git push -u origin <branch>` — apply the **transient-error retry convention** above (a
   first-attempt `git push` commonly fails transiently and succeeds on retry)

> **Stale remote tracking refs warning:** A ref appearing in `git branch -a` as `remotes/origin/<branch>` does NOT guarantee the branch exists on GitHub. Always verify with `gh api repos/<owner>/<repo>/branches/<branch>` or `git ls-remote origin <branch>` before assuming the push succeeded.
>
> **Push rejected (non-fast-forward) with diverged history:** If `git push` fails with non-fast-forward and the remote branch contains commits from a different plan (plan ID reuse or prior aborted execution), **force-push** with `git push -f -u origin <branch>`. This is safe because the plan branch is private to this plan's execution and any diverged remote state is stale.

### 2.5. Upload Artifacts

**If custom options exist and `includeArtifacts` is `false`, skip this step entirely** (set artifact markdown to empty).

Otherwise, if an artifact upload tool is available in `Tools/`, run it to upload screenshots and videos from `<TendrilPlanFolder>/Artifacts/` to persistent storage.

Capture the returned markdown. If non-empty, it will be appended to the PR body under an `## Artifacts` heading in the next step. If no upload tool is available, skip this step.

### 2.6. Verify Branch Is Visible on the Remote

Before creating the PR, confirm GitHub's API actually sees the freshly pushed branch. There
is a short propagation lag between a successful `git push` and the branch being queryable via
the API — creating the PR too early is a primary cause of intermittent first-attempt
failures ("No commits between ..." / "head branch not found") that then succeed on retry.

For each pushed branch, poll briefly until the branch is visible:

```bash
for i in $(seq 1 5); do
  if gh api "repos/<owner>/<repo>/branches/<branch>" >/dev/null 2>&1; then break; fi
  sleep 2
done
```

If the branch is still not visible after polling, re-push (`git push -u origin <branch>`,
applying the transient-error retry convention) and poll once more before proceeding.

### 3. Create PR

Report status: `tendril job status TendrilJobId --message="Creating pull request..."`

Apply the **transient-error retry convention** to every `gh` command in this step (and steps
3.5–6): retry transient/network/`5xx`/`429` failures up to 3 times with backoff; fail fast on
genuine errors (auth, invalid repo, validation).

For each pushed branch:

```bash
gh pr create [--draft] --repo <owner/repo> --base <default-branch> --head <branch> --title "<title>" --body "$(cat <<'EOF'
<body content>
EOF
)"
```

- **Base branch:**
  1. Read plan.yaml and get the project name
  2. For each repo, check the firmware header for `baseBranch` configuration
  3. If configured, use that value
  4. Otherwise, auto-detect via: `gh repo view <owner/repo> --json defaultBranchRef -q .defaultBranchRef.name`
- **Title:** `[<planId>] <plan title>`
- **Body:** 
  1. **If SourceUrl is present in firmware header** and it's a GitHub issue URL (format: `https://github.com/owner/repo/issues/NUMBER`), prepend `Fixes #NUMBER\n\n` to the body
  2. If `<TendrilPlanFolder>/Artifacts/summary.md` exists, use its content as the PR body (after the issue link)
  3. Otherwise, fall back to summary from Problem + Solution sections
  4. Append commit list
  5. If `$artifactMarkdown` from step 2.5 is non-empty, append it under an `## Artifacts` heading

  **Issue linking logic:**
  ```bash
  # Extract issue number from SourceUrl (if present in firmware header)
  issueLink=""
  if [[ -n "$SOURCE_URL" && "$SOURCE_URL" =~ github\.com/.*/issues/([0-9]+) ]]; then
    issueNumber="${BASH_REMATCH[1]}"
    issueLink="Fixes #${issueNumber}\n\n"
  fi

  # Construct body with issue link prepended
  body="${issueLink}${summaryContent}\n\n---\n${commitsList}${artifactMarkdown}\n\n---\nCreated using [Ivy Tendril](https://ivy.app)."
  ```
- **Draft (custom options):** If custom options exist and `draft` is `true`, add `--draft` to the `gh pr create` command to create the PR in draft mode. If no custom options or `draft` is `false`, create as ready for review (default behavior).
- **Reviewer (custom options):** If custom options exist and `reviewer` is non-empty, add `--reviewer <reviewer>` to the `gh pr create` command.

### 3.5. Add PR Comment (custom options)

If custom options exist and `comment` is non-empty, after creating each PR run:

```bash
gh pr comment <pr-number> --repo <owner/repo> --body "<comment>"
```

If no custom options or `comment` is empty, skip this step.

### 3.7. Resolve Merge Conflicts (if enabled)

If `PrSolveMergeConflicts` is `true` (default), check each PR for merge conflicts and resolve them proactively:

Report status: `tendril job status TendrilJobId --message="Checking for merge conflicts..."`

For each PR created in step 3:

```bash
# Poll mergeability (GitHub computes it asynchronously)
for i in $(seq 1 6); do
  MERGEABLE=$(gh pr view <pr-number> --repo <owner/repo> --json mergeable -q '.mergeable')
  if [[ "$MERGEABLE" != "UNKNOWN" ]]; then break; fi
  sleep 5
done
```

| Mergeable status | Action |
|---|---|
| `MERGEABLE` | No action needed, proceed to step 4 |
| `CONFLICTING` | **Resolve conflicts** (see below), then continue |
| `UNKNOWN` (after 30s timeout) | Proceed to step 4 (assume clean) |

#### Conflict Resolution

When the PR status is `CONFLICTING`, resolve the conflict locally:

1. **Locate the worktree** for this repo. If the worktree still exists in `<TendrilPlanFolder>/Worktrees/<repo-folder-name>`, use it. If the worktree was already removed, use the original repo path — create or force-update the local branch first: `git branch -f <branch-name> <sha>` and `git checkout <branch-name>`.

2. **Read the plan revision** to understand the intent of the plan's changes (what matters, what can be safely adapted).

3. **Merge the base branch** into the feature branch:
   ```bash
   cd <worktree-or-repo-path>
   git fetch origin <default-branch>
   git merge origin/<default-branch>
   ```

4. **Resolve conflicts**: Read each conflicted file (`git diff --name-only --diff-filter=U`), understand both sides, and edit to resolve. Prioritize:
   - Keep the plan's intentional changes
   - Accept base branch changes for unrelated code
   - When both sides changed the same lines, merge intelligently based on the plan's intent

5. **Commit the merge**:
   ```bash
   git add -A
   git commit -m "[<planId>] Resolve merge conflicts with <default-branch>"
   ```

6. **Quick build check** (if build-critical files were involved in conflicts):
   ```bash
   # Fetch build command: tendril verification get Build
   ```
   If the build fails, fix the issue and amend the merge commit.

7. **Push** the resolved branch:
   ```bash
   git push origin <branch>
   ```

8. **Re-check mergeability** (poll up to 30s again). If now `MERGEABLE`, proceed to step 4. If still `CONFLICTING` after resolution, **fail with a detailed error** explaining which files could not be resolved.

**Important:** Only attempt conflict resolution **once**. If the second mergeability check still shows CONFLICTING, fail the execution — infinite retry loops waste tokens and time.

This step runs regardless of whether a merge will be performed in step 4. Even if `PrMerge` is `false`, resolving conflicts ensures the PR is ready for manual review or future merging.

If `PrSolveMergeConflicts` is `false`, skip this step entirely — the PR may be left in a conflicting state.

### 4. Merge (only if `PrMerge` is `true`)

Report status: `tendril job status TendrilJobId --message="Applying merge options..."`

**!STOP — MERGE GATE. The single deciding factor is the `PrMerge` firmware header value. Nothing else.**

- **If `PrMerge` is `false`: do NOT merge.** Do **not** run `gh pr merge` under any circumstances. The PR stays open for manual review. Skip directly to step 5. (Do not look for any other rule or signal — there is none.)
- **If `PrMerge` is `true` (or the flag is absent):** merge the PR using the flags below.

**Note:** Merge conflict resolution is handled in step 3.7 if `PrSolveMergeConflicts` was `true`. By this step, the PR should already be conflict-free (if step 3.7 ran successfully).

When merging, build the command from the flags:
- Always pass `--merge --admin`.
- Add `--delete-branch` **only if `PrDeleteBranch` is `true`** (default `true`).

```bash
gh pr merge <pr-number> --repo <owner/repo> --merge [--delete-branch] --admin
cd <original-repo-path>
# Only pull if the local repo is clean (no uncommitted changes, on the default branch)
if [ -z "$(git status --porcelain)" ] && [ "$(git symbolic-ref --short HEAD)" = "<default-branch>" ]; then
  git pull origin <default-branch>
fi
```

> **Note:** If `--merge` fails with "Merge commits are not allowed", retry with `--squash` instead.

### 5. Clean Up Worktrees

Report status: `tendril job status TendrilJobId --message="Cleaning up worktrees..."`

After a successful merge (`PrMerge: true`), clean up the worktrees to reclaim disk space:

For each repo where the PR was merged:

```bash
tendril plan remove-worktree <TendrilPlanId> <repo-folder-name>
```

**Skip cleanup** when `PrMerge` is `false` — the worktree is still needed for potential review revisions.

If cleanup fails, log a warning but do not fail the overall CreatePr execution.

### 6. Update Plan via CLI

Use the CLI to update the plan — **never edit plan.yaml directly**.

Add each PR URL:

```bash
tendril plan add-pr <plan-id> <pr-url>
```

**Update state to Completed:** If `PrMerge` is `true` and ALL PRs were successfully merged, update the state:

```bash
tendril plan set <plan-id> state Completed
```

If `PrMerge` is `false`, do NOT update the state — the plan remains open for manual review and potential revisions.

### Edge Case: Direct-to-Main (No PR Needed)

Some plans create new repos and push directly to main (e.g., repo scaffolding). These have `repos: []`, no worktrees, and commits already on `origin/main`. When detected:
1. Verify the commit(s) exist on the remote default branch
2. Mark state as Completed: `tendril plan set <plan-id> state Completed`
3. Log outcome as "No PR Required — Direct-to-Main"
4. Skip steps 2–5 entirely

### Rules

- **ALL 7 steps are mandatory** (including 2.5) — do not stop after creating the PR
- One PR per repo worktree that has commits
- Skip worktrees with no commits ahead of the base branch
- Use `gh` CLI for all GitHub operations
- **Retry transient failures** per the transient-error retry convention before giving up
- **Accurate failure reporting:** if a step ultimately fails, the final error message must
  state the *actual* cause (e.g. `git push to origin failed after 3 retries: <stderr>` or
  `gh pr create failed: <stderr>`). Never phrase a git/GitHub/network failure as a Claude
  usage, quota, or rate-limit problem — that misleads the user about what to fix.
- NEVER embed images via GitHub branch URLs (`github.com/blob/<branch>/...`) — these 404 after branch deletion. All screenshots/images in PR bodies must use persistent storage URLs (from the artifact upload tool, if available).

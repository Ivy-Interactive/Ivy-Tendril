# Verification Pipeline Reference

How Tendril's plan verification and checking works end-to-end.

## Verification Lifecycle

```
Plan Revision checkboxes (- [x] Build, - [ ] Tests)
    ↓
config.yaml defines verification prompts
    ↓
ExecutePlan Step 7 runs each checked verification
    ↓
Up to 3 fix-retry cycles per verification
    ↓
Results written to verification/{Name}.md
    ↓
Status set via: tendril plan set-verification <id> <name> <status>
    ↓
Valid statuses: Pending, Pass, Fail, Skipped
```

## Pre-Execution Checks (ExecutePlan Steps 1.5–1.8)

### Step 1.5 — Dependency Verification
- Reads `dependsOn` from plan.yaml
- For each dependency plan: checks state is `Completed` and PRs are `MERGED`
- Uses `gh pr view` to verify PR merge status
- **Common failure**: dependency plan completed but PR not yet merged

### Step 1.6 — Worktree Isolation Validation
- Ensures target repos are not already worktrees
- Ensures plans directory is not inside a git worktree
- **Common failure**: stale worktree from a previous failed execution

### Step 1.7 — Code State Validation
- Scans plan revisions for `**Current implementation**` code blocks
- Validates those blocks still match the actual codebase files
- Writes `verification/PreExecution.md`
- Also detects self-flagged redundancy (plan says it's already done)
- **Common failure**: code changed between plan creation and execution

### Step 1.8 — Auto-Commit Uncommitted Changes
- Detects dirty files in target repos
- Commits and pushes before worktree creation
- Includes stale-file detection
- **Common failure**: uncommitted changes in unexpected repos

## Post-Execution Verification

### JobService.VerifyCreatePlanResult
- Runs after CreatePlan agent exits with code 0
- Checks for `"Plan created: <folder>"` marker in agent output
- Verifies plan folder exists on disk (`FindPlanFolderById`)
- Falls back to checking trash (`FindTrashEntryById`)
- Can change `Completed → Failed` if verification fails

### JobService.CheckDependencies
- Called by `TryBlockForDependencies` for ExecutePlan jobs
- Delegates to `JobCompletionHandler.CheckDependencies(planFolder)`
- Verifies dependency plans are `Completed` and PRs are merged
- Sets job to `Blocked` if unmet, transitions plan to `Blocked` state

### Resume-vs-Redo Logic (ExecutePlan)
On re-execution, checks if prior run is intact:
- Commits populated
- All verifications pass
- Reports exist
- Worktree clean
If all pass → resumes from where it left off rather than redoing

## Inline vs Delegated Verifications

Verifications come in two forms:

### Inline Verifications
- Defined only in `config.yaml` with a `prompt:` field
- Executed directly by the ExecutePlan agent within its session
- Examples: `DotnetBuild`, `DotnetTest`

### Delegated Verifications
- Have a matching directory under `Promptwares/{VerificationName}/`
- Must be run as a separate process via `tendril promptware {Name}`
- The ExecutePlan agent CANNOT self-certify these as Pass
- Examples: `IvyFrameworkVerification` (creates a sample app, runs Playwright tests)

### How to detect delegation
Check if a directory exists at `Promptwares/{VerificationName}/` — if it does, it's delegated. This is enforced in:
- `Update-PlanYaml.ps1` — blocks `-SetVerification Name=Pass` when a matching promptware directory exists
- `Promptwares/ExecutePlan/Program.md` — instructs the agent to check config.yaml's promptwares section

### Self-certification red flags
When debugging, if you see a delegated verification marked Pass:
1. Check `verification/{Name}.md` — does it describe actual test execution or just code review?
2. Check the JSONL for `tendril promptware {Name}` invocations — were they attempted?
3. Check for CLI errors that might have prevented the agent from invoking the promptware

## Common Verification Gaps

1. **CheckResult too lenient**: Verification passes despite missing deliverables (e.g., planned file not created)
2. **Verification doesn't cross-reference plan revision**: Status set to Pass without checking all acceptance criteria
3. **Stale worktree state**: Worktree from previous failed run not cleaned up properly
4. **Dependency race**: Plan unblocked before dependency PR is actually merged (GitHub API lag)
5. **Delegated verification self-certified**: Agent bypasses the separate promptware and marks it Pass directly (see common-failure-modes.md)

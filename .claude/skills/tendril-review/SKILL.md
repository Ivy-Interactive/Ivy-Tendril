---
name: tendril-review
description: Comprehensive post-change review of all modified files. Checks for code smells, cleanup opportunities, unnecessary legacy support, missing tests, broken tests, and obsolete tests. Use after a big change to leave the codebase in better health.
---

# tendril-review

Perform a thorough review of all changes in the current working tree (or a specified branch/PR). The goal is to leave every touched file in better code health than before.

## Invocation

```
/tendril-review
```

No arguments required — operates on the current git diff against the base branch.

## What This Skill Does

1. Identifies ALL changed files (staged, unstaged, and committed on branch)
2. Reviews each file for code smells, cleanup opportunities, and quality issues
3. Flags unnecessary legacy/backwards-compatibility code (asks before removing)
4. Checks test coverage — missing tests, broken tests, obsolete tests
5. Runs the test suite and reports failures
6. Produces actionable recommendations

## Execution Steps

### Phase 1 — Scope the Changes

1. Run `git diff --name-only` against the base branch to get all changed files
2. Run `git status` to capture any uncommitted changes
3. Categorize files: source code, tests, config, promptware, other
4. Flag any files that seem unrelated to the main change — **ask the user** if there's confusion about why they changed

### Phase 2 — Code Quality Review

For each changed source file:

1. Read the full file content
2. Check for:
   - Dead code or unused imports
   - Code duplication
   - Overly complex methods (consider cyclomatic complexity)
   - Poor naming or unclear intent
   - Missing error handling at system boundaries
   - Inconsistent patterns vs. the rest of the codebase
   - Unnecessary abstractions or over-engineering
   - Legacy/backwards-compatibility code that may no longer be needed

3. Use `mcp__codescene__code_health_review` on each changed source file to get an objective code health assessment

**Important:** For any legacy support or backwards-compatibility code identified for removal — **ASK the user before removing**. Do not auto-delete.

### Phase 3 — Test Review

1. Identify tests related to changed code
2. Run the full test suite: `dotnet test` (or appropriate command)
3. Report:
   - **Failing tests** — investigate root cause
   - **Missing tests** — suggest what should be covered
   - **Obsolete tests** — tests that exercise removed/changed behavior and are no longer relevant
4. For obsolete tests — **ASK the user** before suggesting removal

### Phase 4 — Summary Report

Produce a structured report:

```
## Review Summary

### Changes Reviewed
- List of all files reviewed, grouped by category

### Issues Found
- [ ] Issue 1 — severity, file, description
- [ ] Issue 2 — ...

### Cleanup Opportunities
- [ ] Opportunity 1 — what and why

### Legacy Code Questions
- [ ] "This code appears to exist for backwards compat with X — still needed?"

### Test Status
- Passing: N
- Failing: N (with details)
- Missing coverage: list
- Potentially obsolete: list

### Recommendations
Prioritized list of actions to take
```

## Principles

- **Leave it better than you found it** — every touched file should improve in code health
- **Ask, don't assume** — when in doubt about whether something is still needed, ask
- **Unrelated changes are normal** — the user may have made parallel manual edits; clarify rather than flag as errors
- **Be thorough** — this is a big change review, not a quick scan

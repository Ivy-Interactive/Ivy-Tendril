# CreateIssue

Create a GitHub issue from a plan.

## Context

The firmware header contains:
- **TendrilPlanFolder** - path to the plan folder
- **CurrentTime** - current UTC timestamp
- **Repo** - target repository path (local path)
- **Assignee** - GitHub username to assign (optional, may be empty)
- **Comment** - optional comment to include in the issue body (may be empty)

## Execution Steps

### 1. Read Plan

- Read `plan.yaml` and the latest revision for the plan title, Problem, Solution, and Tests sections together in a single batched read operation to reduce spin-up overhead
- Report plan context to Jobs UI: `tendril job status TendrilJobId --message="Creating issue..." --plan-id=<plan-id> --plan-title="<title>"`

### 2. Identify GitHub Repository

From the `Repo` path:
```bash
cd <Repo>
gh repo view --json nameWithOwner --jq ".nameWithOwner"
```

If this fails, report that the repo is not a GitHub repository and stop.

### 3. Create Issue

Use the plan's title and revision sections to create a well-formatted issue:

```bash
gh issue create --repo <owner/repo> --title "<title>" --body "<body>"
```

- **Title:** Plan title
- **Body:** Markdown-formatted body containing:
  - `## Problem`: The problem statement from the plan revision.
  - `## Proposed Solution`: The technical approach and affected files from the plan's Solution section (if present).
  - `## Verification & Tests`: The test scope and verification criteria from the plan's Tests section (if present).
  - A footer linking back to the Tendril Plan ID (e.g. `*Created from Tendril Plan <plan-id>*`).
  - If `Comment` is non-empty, append it under an `## Additional Context` heading separated by a horizontal rule (`---`).
- **Assignee:** If provided, add `--assignee <Assignee>`

### 4. Update plan.yaml

The issue URL should be noted in the output for the user.

### Rules

- Do NOT modify any source code
- Use `gh` CLI for all GitHub operations
- If the repo has no GitHub remote, fail with a clear message


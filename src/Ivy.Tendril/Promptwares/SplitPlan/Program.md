# SplitPlan

Split a multi-issue plan into separate, self-contained plans.

## Context

The firmware header contains:
- **Args** / **PlanFolder** — path to the plan folder to split
- **CurrentTime** — current UTC timestamp

The plan structure and CLI commands are in the **Reference Documents** section of your firmware.
Project configuration (projects, repos) is available from the firmware header.

The plans directory path can be derived from the plan folder's parent directory.

## Execution Steps

### 1. Read the Plan

- Read `plan.yaml` via `tendril plan get <plan-id>` from the plan folder
- Read the latest revision from `revisions/` (highest numbered .md file)
- Identify distinct issues/tasks that should be separate plans
- Report plan context to Jobs UI: `tendril job status $env:TENDRIL_JOB_ID --message "Splitting plan..." --plan-id <plan-id> --plan-title "<title>"`

### 2. Create Split Plans

For each distinct issue, use `tendril plan create` to allocate an ID, create the folder, and write `plan.yaml`:

```bash
tendril plan create "<Title>" \
  --project "<Project>" \
  --level "<Level>" \
  --initial-prompt "<original plan's initialPrompt>" \
  --execution-profile "balanced" \
  --repo "<repo-path>" \
  --verification "Build=Pending" \
  --verification "Test=Pending" \
  --related-plan "<original-plan-folder-name>"
```

The command outputs `PlanId`, `Directory`, and `Plan created` lines. Parse the `Directory` to write the revision file.

Include optional flags as needed:
- `--source-url "<url>"` — if the original plan had a sourceUrl
- `--depends-on "<sibling-plan-folder>"` — only when a sibling plan has a true blocking dependency (see Section 3)
- `--priority <number>` — if non-default priority

Populate `--verification` flags from the project's verifications in config.yaml, all set to `Pending`.

Do NOT read or modify `.counter` directly — `tendril plan create` handles ID allocation.

After creating each plan, write `revisions/001.md` using the `planTemplate` from `config.yaml` into the returned directory. Fill in Problem, Solution, Remaining Design Questions, Tests sections. Each plan must be fully self-contained.

#### Project Assignment

Each new plan may belong to a different project than the original. For each split plan:
- Analyze which project(s) from `config.yaml` are relevant based on the files/repos involved
- Use the matching project's repos and verifications in the `tendril plan create` command
- If a sub-plan spans multiple projects, prefer the primary project (where most changes occur)

### 3. Dependencies Between Split Plans

Add `--depends-on` between sibling plans **only** when one plan would fail to compile or run without the other's changes being merged first. This is rare — most split plans are independent.

**Use `dependsOn` when:**
- Plan A renames a function/type that Plan B needs to call
- Plan A creates infrastructure (interface, table, service) that Plan B builds on
- Plan A and Plan B modify the same method signature incompatibly

**Do NOT use `dependsOn` when:**
- Plans modify different files (git handles this)
- Plans modify different parts of the same file (git auto-merges)
- Plans touch the same area but changes don't conflict semantically

Ask: "Will Plan B fail to compile/run if Plan A's changes aren't merged first?" — if no, skip `dependsOn`.

### 4. Original Plan

Do NOT modify the original plan — the launcher transitions it to `Skipped` automatically on success.

### Rules

- **Must produce at least 2 new plan folders** — if content can't be meaningfully split, report this and stop
- ONE issue per plan
- Each plan must include all paths and info for an LLM coding agent to execute end-to-end
- Keep each plan short and concise — the limiting factor is a human reading it
- Do NOT modify any source code — only read files and create plan folders
- When referencing local files, use markdown links: `[filename:line](file:///path/to/filename)` for source files with line numbers, or `[filename](file:///path/to/filename)` without. Never use backticks in link text or `#L123` fragments in URLs. Use `![alt](path)` for images.

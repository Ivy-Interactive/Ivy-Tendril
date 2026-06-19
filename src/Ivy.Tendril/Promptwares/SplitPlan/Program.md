# SplitPlan

Split a multi-issue plan into separate, self-contained plans.

## Context

The firmware header contains:
- **TendrilPlanFolder** — path to the plan folder to split
- **CurrentTime** — current UTC timestamp

The plan structure and CLI commands are in the **Reference Documents** section of your firmware.
Project configuration (projects, repos) is available from the firmware header.

The plans directory path can be derived from the plan folder's parent directory.

## Execution Steps

### 1. Read the Plan

- Read `plan.yaml` via `tendril plan get <plan-id>` from the plan folder
- Read the latest revision from `Revisions/` (highest numbered .md file)
- Identify distinct issues/tasks that should be separate plans
- Report plan context to Jobs UI: `tendril job status TendrilJobId --message "Splitting plan..." --plan-id <plan-id> --plan-title "<title>"`

### 2. Create Split Plans

Report status: `tendril job status TendrilJobId --message "Creating split plans..."`

For each distinct issue, use `tendril plan create` to allocate an ID, create the folder, and write `plan.yaml`:

**Title format:** `<Title>` MUST be human-readable **Title Case with spaces** and normal capitalization. Do NOT use the PascalCase / no-space folder form — the CLI derives the folder `SafeTitle` from your title automatically.
- ✅ `Show File Details in Local Changes Dialog`
- ❌ `ShowFileDetailsInLocalChangesDialog`

```bash
tendril plan create "<Title>" "<TendrilProject>" \
  --plans-dir "<TendrilPlansFolder>" \
  --level "<Level>" \
  --initial-prompt "<original plan's initialPrompt>" \
  --execution-profile "balanced" \
  --verification "Build=Pending" \
  --verification "Test=Pending" \
  --related-plan "<original-plan-folder-name>"
```

**IMPORTANT:** Always pass `--plans-dir` with the plans directory (derive from the plan folder's parent). This ensures child plans are created in the correct directory regardless of environment variable inheritance. Repos are derived automatically from the project configuration.

The command outputs `PlanId`, `Directory`, and `Plan created` lines. Parse the `Directory` to write the revision file.

Include optional flags as needed:
- `--source-url "<url>"` — if the original plan had a sourceUrl
- `--depends-on "<sibling-plan-folder>"` — only when a sibling plan has a true blocking dependency (see Section 3)
- `--priority <number>` — if non-default priority

Populate `--verification` flags from the project's verifications in the **Projects** section, all set to `Pending`.

Do NOT read or modify `.counter` directly — `tendril plan create` handles ID allocation.

After creating each plan, write the revision via CLI:

```bash
tendril plan write-revision <PlanId> <<'EOF'
<revision content here>
EOF
```

**The revision's first line is the `# {title}` H1 heading — it MUST be the exact same string you passed as `<Title>` to `tendril plan create` for that plan** (human-readable Title Case, not the PascalCase folder form). The `plan.yaml` title and the spec H1 must always match.

The command reads from STDIN and auto-creates the next numbered revision file. Fill in Problem, Solution, Remaining Design Questions, Tests sections. Each plan must be fully self-contained. Do NOT use the Write or Edit tools to create revision files directly in `Revisions/`.

#### Project Assignment

Each new plan may belong to a different project than the original. For each split plan:
- Analyze which project(s) from the **Projects** section are relevant based on the files/repos involved
- Use the matching project's repos and verifications in the `tendril plan create` command
- If a sub-plan spans multiple projects, prefer the primary project (where most changes occur)

### 3. Dependencies Between Split Plans

Report status: `tendril job status TendrilJobId --message "Setting up dependencies..."`

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

Report status: `tendril job status TendrilJobId --message "Updating original plan..."`

Do NOT modify the original plan — the launcher transitions it to `Skipped` automatically on success.

### Rules

- **Must produce at least 2 new plan folders** — if content can't be meaningfully split, report this and stop
- ONE issue per plan
- Each plan must include all paths and info for an LLM coding agent to execute end-to-end
- Keep each plan short and concise — the limiting factor is a human reading it
- Do NOT modify any source code — only read files and create plan folders
- When referencing local files, use markdown links: `[filename:line](file:///path/to/filename)` for source files with line numbers, or `[filename](file:///path/to/filename)` without. Never use backticks in link text or `#L123` fragments in URLs. Use `![alt](path)` for images.

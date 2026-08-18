# UpdatePlan

Update an existing plan by applying user instructions from the firmware header.

## Context

The firmware header contains:
- **TendrilPlanFolder** — path to the plan folder
- **UpdateInstructions** — the user's update instructions (what to change)
- **CurrentTime** — current UTC timestamp

The plan structure and CLI commands are in the **Reference Documents** section of your firmware.
Project configuration is available from the firmware header.

## Execution Steps

### 1. Read the Plan

- Read the latest revision: `tendril plan get-revision <TendrilPlanId>`
- Get the plan title: `tendril plan get <TendrilPlanId> title`
- Report plan context to Jobs UI: `tendril job status TendrilJobId --message="Updating plan..." --plan-id=<plan-id> --plan-title="<title>"`

### 2. Parse Instructions

Read the `UpdateInstructions` value from the firmware header. Instructions are either:
- **Questions** (contain `?` or start with question words) — research and answer them
- **Instructions** — changes to incorporate into the plan

**File attachments:** If `UpdateInstructions` contains `[file: <absolute-path>]` markers, these are user-uploaded attachments (screenshots, code samples, documents). Read and inspect them for context. When writing the updated revision, format images or screenshots as markdown images using `file:///` URLs with forward slashes (e.g., `![Description](file:///C:/path/to/image.png)`).

### 3. Research and Answer Questions

Report status: `tendril job status TendrilJobId --message="Researching questions..."`

For each question in the instructions:
1. Read relevant source files to find the answer. Scope `grep_search` to specific subdirectories (e.g. `src/`) and file patterns (`*.cs`, `*.tsx`, etc.), avoiding unconstrained root searches over build artifacts.
2. Use the firmware header for project context if needed

### 3.5. Retire Answered Questions

This is where the user's answers land: the UI writes them back into the revision markdown, and the user then runs UpdatePlan. The schema and answer semantics are in the **Question Blocks** section of **Reference Documents**.

1. Scan the revision markdown for every `questions` fence. There may be several, anywhere in the document.
2. For every question whose block has an `answer` key with a non-null value, treat it as a decision by the user. Fold it into whichever section it sits nearest to (or `## Problem` / `## Solution` for a scope-level question) as concrete prose or steps, and **delete that question from its block**. Never restate it as an open question, and never leave an answered question in a revision.
3. For every question with `answer: null` (declined), make the call yourself, write one sentence near where the block sits saying which way you went and that the user deferred, then delete the question.
4. A question the user answered in prose in `UpdateInstructions` counts as answered even though the markdown has no `answer` key. Same handling.
5. Carry every question with no `answer` key forward verbatim — same `header`, `options`, ordering, and position in the document. Do not reword it, and do not add an `answer`.
6. If a block's last question is retired, drop that fence entirely. If new ambiguity appeared, add new blocks, each with 4 or fewer questions, placed next to the section it concerns.
7. The old prose `## Questions` section is gone. If a prior revision has one, convert the still-open items into `questions` block(s) and fold the rest into the plan.

### 4. Apply Changes

Report status: `tendril job status TendrilJobId --message="Applying changes..."`

- Write the new revision via CLI (number auto-incremented):
  ```bash
  tendril plan write-revision <plan-id> --stdin <<'EOF'
  <updated revision content here>
  EOF
  ```

  The command reads from STDIN and auto-creates the next numbered revision file. Do NOT use the Write or Edit tools to create revision files directly in `Revisions/`.
- Incorporate the intent of each instruction into the updated plan
- Carry the `questions` blocks forward as decided in step 3.5. `write-revision` rejects a malformed block and writes nothing; fix the reported lines and retry.
- Preserve the plan template structure
- The updated plan must be at least as comprehensive as the original

### Rules

- Do NOT modify any source code — only read files and update the plan
- Do NOT modify the original revision — always create a new revision file
- Do NOT modify `plan.yaml` — the launcher script handles state and timestamps
- The plan must remain self-contained with all paths and information for an LLM coding agent
- Keep the plan short and concise — the limiting factor is a human reading it
- When referencing local files, use markdown links: `[filename:line](file:///path/to/filename)` for source files with line numbers, or `[filename](file:///path/to/filename)` without. Never use backticks in link text, and never append a line number to the URL itself — no `:348` suffix and no `#L123` fragment; the line number belongs only in the display text. Only use `file:///` links for files that already exist; for a file the plan will create, write its path in inline code (`` `path/to/new/file` ``) instead of a link. Use `![alt](path)` for images.

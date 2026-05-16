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

- Read the latest revision from `revisions/` (highest numbered .md file)
- Get the plan title: `tendril plan get <TendrilPlanId> title`
- Report plan context to Jobs UI: `tendril job status TendrilJobId --message "Updating plan..." --plan-id <plan-id> --plan-title "<title>"`

### 2. Parse Instructions

Read the `UpdateInstructions` value from the firmware header. Instructions are either:
- **Questions** (contain `?` or start with question words) — research and answer them
- **Instructions** — changes to incorporate into the plan

### 3. Research and Answer Questions

For each question in the instructions:
1. Read relevant source files to find the answer
2. Use the firmware header for project context if needed

### 3.5. Resolve Answered Questions

Compare each existing question in `## Questions` against:
- The user's instructions (user may have directly answered a question)
- Your research findings from step 3

For each question, determine if it has been answered — either explicitly by the user's instructions or implicitly by a decision made in the updated plan. If answered:
- Wrap the question in a `<details>` block (collapsed) with the answer as the body
- The answer should reference the user's instruction or the design decision that resolves it

If all questions are resolved and no new questions arose, omit the `## Questions` section entirely.

### 4. Apply Changes

- Write the new revision via CLI (number auto-incremented):
  ```bash
  tendril plan write-revision <plan-id> <<'EOF'
  <updated revision content here>
  EOF
  ```

  The command reads from STDIN and auto-creates the next numbered revision file. Do NOT use the Write or Edit tools to create revision files directly in `revisions/`.
- Incorporate the intent of each instruction into the updated plan
- Maintain the `## Questions` section (placed after the title, before `## Problem`) using `<details>` tags: (1) Existing questions answered by the user's instructions or research should be collapsed into `<details>` blocks with the answer. (2) New questions become new `<details>` blocks with answers. (3) Unanswered questions from prior revisions remain as open items (not in `<details>`). (4) If all questions are resolved and no new ones arose, omit the section entirely. Format:
  ```html
  <details>
  <summary>Question</summary>
  Answer
  </details>
  ```
- Preserve the plan template structure
- The updated plan must be at least as comprehensive as the original

### Rules

- Do NOT modify any source code — only read files and update the plan
- Do NOT modify the original revision — always create a new revision file
- Do NOT modify `plan.yaml` — the launcher script handles state and timestamps
- The plan must remain self-contained with all paths and information for an LLM coding agent
- Keep the plan short and concise — the limiting factor is a human reading it
- When referencing local files, use markdown links: `[filename:line](file:///path/to/filename)` for source files with line numbers, or `[filename](file:///path/to/filename)` without. Never use backticks in link text or `#L123` fragments in URLs. Use `![alt](path)` for images.

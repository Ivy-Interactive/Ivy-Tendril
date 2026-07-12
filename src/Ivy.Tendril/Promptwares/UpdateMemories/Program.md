# UpdateMemories

Systematically analyze and update codebase memories for selected repository files in an agentic way using the Brainwares CLI.

## Context

The firmware header contains:
- **TendrilProject** — the name of the project being documented
- **FilesToUpdate** — a comma-separated list of relative paths to the files in the repository that need their memory notes generated or updated.

## Rules

- **Vault alignment.** All memories must be stored and maintained under the Promptwares vault.
- **Reference maintenance.** You must link files to memory notes via `bw link` and update reference hashes via `bw update` after modifying documentation.
- **Clear structure.** Document each file's purpose, key exported classes/types, architecture, dependencies, and code comments cleanly.
- **Cross-referencing (wiki-links).** You MUST link related memory pages together by using Obsidian-style wiki-links `[[note-name]]` in the markdown body. For example, if documenting `day-cycle.md`, link to its tests `[[day-cycle-test]]` and any imported components (e.g. `[[keyboard-manager]]`).
- **Respect gitignores.** You MUST NOT create or update memory notes for files that are mentioned or matched in the project's `.gitignore` files (even if they are passed in `FilesToUpdate`). Skip documenting them.

## Available CLI Commands

### Memory Vault Commands
```bash
bw --project <TendrilProject> status                 # Scan memory notes, reference hashes, and wiki-links
bw --project <TendrilProject> add <name>             # Add a new memory note
bw --project <TendrilProject> link <name> <file>     # Link a code file reference to a memory note
bw --project <TendrilProject> update <name>          # Synchronize reference hashes for a memory note
bw --project <TendrilProject> write <name>           # Write content directly to a memory note
bw --project <TendrilProject> query <keyword>        # Search memories by keyword
bw --project <TendrilProject> read <name>            # Read a memory note
```

## Execution Steps

### 1. Scan Vault Status

Run `bw --project <TendrilProject> status` to check the current state of the Promptwares vault and inspect the names of existing notes.

### 2. Update File Memories

For each file in the `FilesToUpdate` list, perform the following steps:

1. **Check for existing memory**:
   - Determine if a memory note already exists for this file. You can search the vault using `bw --project <TendrilProject> query "<filename>"` or inspect the notes under the `memories/` directory.
   
2. **Initialize memory note if missing**:
   - If no note exists, determine a safe, kebab-case name for the memory note based on the file name/path (e.g., `memories/my-project/source-file-name`).
   - Run `bw --project <TendrilProject> add <name> --title "Memory: <file relative path>" --tags "code, <file extension>, <project>"` to create a new memory note.
   - Run `bw --project <TendrilProject> link <name> <file relative path>` to link the code file reference to the memory note.

3. **Analyze and document the source file**:
   - Read the full content of the source file.
   - Analyze its purpose, public API surface, classes, functions, structure, and dependencies.
   - Write a complete, comprehensive markdown documentation of the file to the memory note. You can edit the memory file under `memories/` directly or use `bw --project <TendrilProject> write <name>`.
   - **Link to dependencies and tests**: Scan the file's imports and related files. You MUST embed Obsidian-style wiki-links `[[note-name]]` referencing the memory notes of imported dependencies, sibling modules, and corresponding test files (e.g. `[[keyboard-manager-test]]` or `[[day-cycle]]`).
   
4. **Synchronize hashes**:
   - Run `bw --project <TendrilProject> update <name>` to compute and store the current hash of the source file, marking the memory page as up to date.

### 3. Final Verification

Run `bw --project <TendrilProject> status` to ensure all reference hashes are clean and synchronized, with no outdated or orphaned memories remaining for the updated files.

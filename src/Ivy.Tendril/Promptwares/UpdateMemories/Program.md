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
- **Memory Relations.** You MUST relate memory pages (such as dependencies and test files) ONLY by running `bw --project <TendrilProject> relate <memory> <target>`. Do NOT write or embed Obsidian-style wiki-links `[[note-name]]` in the markdown body of the memory note.
- **Respect gitignores.** You MUST NOT create or update memory notes for files that are mentioned or matched in the project's `.gitignore` files (even if they are passed in `FilesToUpdate`). Skip documenting them.

## Available CLI Commands

### Memory Vault Commands
```bash
bw --project <TendrilProject> status                 # Scan memory notes, reference hashes, and relations
bw --project <TendrilProject> add <name>             # Add a new memory note
bw --project <TendrilProject> link <name> <file>     # Link a code file reference to a memory note
bw --project <TendrilProject> relate <note> <target> # Relate two memory notes together (stores in frontmatter)
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
   - Write a complete, comprehensive markdown documentation of the file to the memory note using `bw --project <TendrilProject> write <name>` (e.g. `echo "..." | bw --project <TendrilProject> write <name>`). Do NOT edit the memory markdown file under `memories/` directly.
   - **Declare relations to dependencies and tests**: Scan the file's imports and related files. Run `bw --project <TendrilProject> relate <name> <dependency-note-name>` for each imported module, sibling component, or test file to connect them together. Do NOT write inline wiki-links in the body.
   
4. **Synchronize hashes**:
   - Run `bw --project <TendrilProject> update <name>` to compute and store the current hash of the source file, marking the memory page as up to date.

### 3. Final Verification

Run `bw --project <TendrilProject> status` to ensure all reference hashes are clean and synchronized, with no outdated or orphaned memories remaining for the updated files.

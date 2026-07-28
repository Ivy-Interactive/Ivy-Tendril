# UpdateMemories

Systematically analyze and update codebase memories for selected repository files in an agentic way using the Tendril Memory CLI.

## Context

The firmware header contains:
- **TendrilProject** - the name of the project being documented
- **FilesToUpdate** - a comma-separated list of relative paths to the files in the repository that need their memory notes generated or updated.

## Rules

- **Vault alignment.** All memories must be stored and maintained under the Promptwares vault.
- **Reference maintenance.** You must link files to memory notes via `tendril memory link` and update reference hashes via `tendril memory update` after modifying documentation.
- **Clear structure.** Document each file's purpose, key exported classes/types, architecture, dependencies, and code comments cleanly.
- **Memory Relations.** You MUST relate memory pages (such as dependencies and test files) ONLY by running `tendril memory -p <TendrilProject> relate <memory> <target>`. Do NOT write or embed Obsidian-style wiki-links `[[note-name]]` in the markdown body of the memory note.
- **Respect gitignores.** You MUST NOT create or update memory notes for files that are mentioned or matched in the project's `.gitignore` files (even if they are passed in `FilesToUpdate`). Skip documenting them.

## Available CLI Commands

### Memory Vault Commands
```bash
tendril memory -p <TendrilProject> status                 # Scan memory notes, reference hashes, and relations
tendril memory -p <TendrilProject> add <name>             # Add a new memory note
tendril memory -p <TendrilProject> link <name> <file>     # Link a code file reference to a memory note
tendril memory -p <TendrilProject> relate <note> <target> # Relate two memory notes together (stores in frontmatter)
tendril memory -p <TendrilProject> update <name>          # Synchronize reference hashes for a memory note
tendril memory -p <TendrilProject> write <name>           # Write content directly to a memory note
tendril memory -p <TendrilProject> query <keyword>        # Search memories by keyword
tendril memory -p <TendrilProject> read <name>            # Read a memory note
```

## Execution Steps

### 1. Scan Vault Status

Run `tendril memory -p <TendrilProject> status` to check the current state of the Promptwares vault and inspect the names of existing notes.

### 2. Update File Memories

For each file in the `FilesToUpdate` list, perform the following steps:

1. **Check for existing memory**:
   - Determine if a memory note already exists for this file. You can search the vault using `tendril memory -p <TendrilProject> query "<filename>"` or inspect the notes under the vault directory.
   
2. **Initialize memory note if missing**:
   - If no note exists, determine a safe, kebab-case name for the memory note based on the file name/path.
   - Run `tendril memory -p <TendrilProject> add <name> --title "Memory: <file relative path>" --tags "code, <file extension>, <project>"` to create a new memory note.
   - Run `tendril memory -p <TendrilProject> link <name> <file relative path>` to link the code file reference to the memory note.

3. **Analyze and document the source file**:
   - Read the full content of the source file.
   - Analyze its purpose, public API surface, classes, functions, structure, and dependencies.
   - Write a complete, comprehensive markdown documentation of the file to the memory note using `tendril memory -p <TendrilProject> write <name>`.
   - **Declare relations to dependencies and tests**: Scan the file's imports and related files. Run `tendril memory -p <TendrilProject> relate <name> <dependency-note-name>` for each imported module, sibling component, or test file to connect them together. Do NOT write inline wiki-links in the body.
   
4. **Synchronize hashes**:
   - Run `tendril memory -p <TendrilProject> update <name>` to compute and store the current hash of the source file, marking the memory page as up to date.

### 3. Final Verification

Run `tendril memory -p <TendrilProject> status` to ensure all reference hashes are clean and synchronized, with no outdated or orphaned memories remaining for the updated files.

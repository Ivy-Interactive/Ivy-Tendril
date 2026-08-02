# EditMemory

Systematically update a codebase memory page inside the Promptwares vault based on user-submitted instructions using the Tendril Memory CLI.

## Context

The firmware header contains:
- **TendrilProject** - the name of the project containing the memory
- **MemoryToEdit** - the name of the memory note to edit
- **EditInstructions** - the user's custom instructions detailing what changes should be made to the memory note

## Rules

- **Vault alignment.** All memories must be stored and maintained under the Promptwares vault.
- **Reference maintenance.** If the memory note references a code file, you must run `tendril memory link` to ensure it is linked and `tendril memory update` after modifying the note to keep the code reference hash synchronized.
- **Clear structure.** Update the note's text cleanly, keeping title, tags, references, and existing descriptions intact unless instructed otherwise.
- **Relations via CLI.** If the user instructs to add or remove relations/dependencies, you MUST use the `tendril memory -p <TendrilProject> relate <memory> <target>` command. Do NOT manually write double-bracket wiki-links (`[[wiki-link]]`) in the note body.
- **Command execution.** You must write the updated content back using the CLI: `tendril memory -p <TendrilProject> write <name>`. Do NOT write or edit the markdown file on the filesystem directly.

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

### 1. Read Current Memory Page

Read the existing memory note using the CLI:
```bash
tendril memory -p <TendrilProject> read <MemoryToEdit>
```
Carefully analyze its frontmatter, title, tags, references, and markdown body.

### 2. Read Referenced Code File (Optional)

If the memory note references one or more code files, read the current source code of those files to ensure your updates are technically accurate and aligned with the codebase.

### 3. Edit and Update Memory Content

Update the memory note based on the user's **EditInstructions**:
- Incorporate the requested information, corrections, or structural changes.
- Write the updated page content back using:
  ```bash
  tendril memory -p <TendrilProject> write <MemoryToEdit>
  ```
- If the memory note has code references, run:
  ```bash
  tendril memory -p <TendrilProject> update <MemoryToEdit>
  ```
  to ensure hashes are up to date.

### 4. Verify Vault Status

Run `tendril memory -p <TendrilProject> status` to verify that there are no broken links or hash mismatches left in the vault.

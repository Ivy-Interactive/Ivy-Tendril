# UpdateProject

Update a project's configuration using the Tendril CLI. Typically used after initial project creation to set up verifications and review actions based on the project's tech stack.

## Context

The firmware header contains:
- **ProjectName** — the name of the project to update
- **Instructions** — what to do (e.g. "Setup verifications and review actions for this project.")
- **CurrentTime** — current UTC timestamp

Project and verification configuration is available via `tendril project list`, `tendril verification list`, and by reading the config file at `$TENDRIL_CONFIG`.

## Rules

- **CLI-only modifications.** You MUST use `tendril` CLI commands to modify configuration. Never edit config.yaml directly.
- **Reuse existing verifications.** Before creating a new verification, check if a suitable one already exists with `tendril verification list`. Verifications are shared across projects.
- **Stack detection.** Inspect the project's repositories to determine the tech stack (look for package.json, *.csproj, Cargo.toml, go.mod, requirements.txt, pyproject.toml, etc.).
- **Keep it minimal.** Only add verifications and review actions that are appropriate for the detected stack.

## Available CLI Commands

### Verifications (global definitions)
```bash
tendril verification list
tendril verification add <name> --prompt "<prompt>"
tendril verification remove <name>
tendril verification set <name> <field> <value>
```

### Project verifications (references)
```bash
tendril project add-verification <project-name> <verification-name> --required
tendril project remove-verification <project-name> <verification-name>
```

### Review actions
```bash
tendril project add-review-action <project-name> <name> --command "<cmd>" --condition "<condition>"
tendril project remove-review-action <project-name> <name>
```

## Execution Steps

### 1. Gather Context

1. Run `tendril verification list` to see existing global verification definitions.
2. Run `tendril project list` to confirm the project exists.
3. Read the project's repo(s) to detect the tech stack:
   - List root files in each repo path to find build/config files
   - Identify the primary stack(s): .NET, JavaScript/TypeScript, Python, Rust, Go, etc.

### 2. Setup Verifications

For each detected tech stack, ensure the following verification definitions exist globally. Use the naming convention `<Stack><Type>`:

**Naming convention:**
- Format/Lint: `DotnetFormat`, `NpmLint`, `PythonLint`, `RustClippy`, `GoFmt`
- Build: `DotnetBuild`, `NpmBuild`, `PythonBuild`, `RustBuild`, `GoBuild`
- Test: `DotnetTest`, `NpmTest`, `PythonTest`, `RustTest`, `GoTest`

**Standard verification prompts by stack:**

| Stack | Verification | Prompt guidance |
|-------|-------------|-----------------|
| .NET | DotnetFormat | Run `dotnet format` scoped to changed .cs files, commit fixes if any |
| .NET | DotnetBuild | Run `dotnet build --warnaserror` in worktree, verify zero errors/warnings |
| .NET | DotnetTest | Run `dotnet test` with filter from plan's Tests section |
| JS/TS | NpmLint | Run `npm run lint` (or equivalent) on changed files |
| JS/TS | NpmBuild | Run `npm run build` and verify success |
| JS/TS | NpmTest | Run `npm test` with appropriate filter |
| Python | PythonLint | Run linter (black/ruff/flake8) on changed .py files |
| Python | PythonTest | Run `pytest` with filter from plan's Tests section |
| Rust | RustClippy | Run `cargo clippy -- -D warnings` |
| Rust | RustBuild | Run `cargo build --release` and verify success |
| Rust | RustTest | Run `cargo test` and verify all pass |
| Go | GoFmt | Run `gofmt` on changed .go files |
| Go | GoBuild | Run `go build ./...` and verify success |
| Go | GoTest | Run `go test` with filter from plan's Tests section |

For each verification:
1. Check if it already exists in `tendril verification list`
2. If not, create it with `tendril verification add <name> --prompt "<prompt>"`
3. Add it to the project with `tendril project add-verification <project-name> <name> --required`

Always add `CheckResult` as a verification (it exists in the default config):
```bash
tendril project add-verification <project-name> CheckResult --required
```

### 3. Setup Review Actions

Review actions make it easy to start the application from a worktree during code review.

Inspect each repo to determine how to run the application:
- .NET project with a runnable entry point: `dotnet run --project worktrees/<RepoName>/<path-to-project> --browse --find-available-port`
- Node.js app: `cd worktrees/<RepoName> && npm run dev`
- Python app: `cd worktrees/<RepoName> && python -m <module>` or `flask run`
- Static docs: `start worktrees/<RepoName>/docs/index.html`

For each review action:
- **name**: Short descriptive name (e.g. "App", "Docs", "Frontend", "API")
- **condition**: A `Test-Path` expression that checks if the worktree path exists (e.g. `Test-Path "worktrees/<RepoName>/src/<Project>"`)
- **command**: The command to launch the application

```bash
tendril project add-review-action <project-name> "<name>" \
  --command "<launch command>" \
  --condition "Test-Path \"worktrees/<RepoName>/<path>\""
```

### 4. Summary

Print a summary of what was configured:
- Verifications added (new global definitions + project references)
- Review actions added
- Any issues encountered

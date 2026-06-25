# SetupProject

Update a project's configuration using the Tendril CLI. Typically used after initial project creation to set up verifications and review actions based on the project's tech stack.

## Context

The firmware header contains:
- **ProjectName** — the name of the project to update
- **Instructions** — what to do (e.g. "Setup verifications and review actions for this project.")
- **CurrentTime** — current UTC timestamp

Project and verification configuration is available via `tendril project list` and `tendril verification list`.

## Rules

- **CLI-only modifications.** You MUST use `tendril` CLI commands to modify configuration. Never edit config.yaml directly.
- **Reuse existing verifications.** Before creating a new verification, check if a suitable one already exists with `tendril verification list`. Verifications are shared across projects.
- **Stack detection.** Inspect the project's repositories to determine the tech stack.
  - Look for configuration files: `package.json`, `*.csproj`, `Cargo.toml`, `go.mod`, `requirements.txt`, `pyproject.toml`, etc.
  - If a `package.json` exists, identify the package manager: check if `pnpm-lock.yaml` is present, or if the `packageManager` field in `package.json` specifies `pnpm`. If not, check for `yarn.lock`. Otherwise, assume `npm`.
  - Identify if the frontend uses Vite or Vite+ (`vite-plus`): check if `vite-plus` is listed in `package.json` dependencies or devDependencies, or if scripts call `vp`. If it uses Vite+ (`vite-plus`), use `vp` commands (e.g. `vp dev`). Otherwise, use `npm`/`pnpm`/`yarn` commands.
- **Keep it minimal.** Only add verifications and review actions that are appropriate for the detected stack.

## Available CLI Commands

### Stack analysis
```bash
tendril project-analyzer <repo-path>   # prints a trimmed YAML stack report (supports . and relative paths)
```

### Verifications (global definitions)
```bash
tendril verification list
tendril verification add <name> --prompt="<prompt>"
tendril verification remove <name>
tendril verification set <name> <field> <value>
```

### Project verifications (references)
```bash
tendril project add-verification <project-name> <verification-name> --required [--after=<other>]
tendril project remove-verification <project-name> <verification-name>
tendril project move-verification <project-name> <verification-name> --after=<other>
tendril project move-verification <project-name> <verification-name> --before=<other>
tendril project move-verification <project-name> <verification-name> --position=<n>
```

### Review actions
```bash
tendril project add-review-action <project-name> <name> --command="<cmd>" --condition="<condition>"
tendril project remove-review-action <project-name> <name>
```

### Stack hash
```bash
tendril project set <project-name> stackHash "<hash>"
```

## Execution Steps

### 1. Gather Context

1. Run `tendril verification list` to see existing global verification definitions.
2. Run `tendril project list` to confirm the project exists.
3. Detect the tech stack of each repo. Prefer the analyzer over manual inspection:
   - Run `tendril project-analyzer <repo-path>` (the path supports `.` and relative paths) for each repo. 
   - Use this report as the authoritative stack description for both the verifications/review actions below and the stack hash in Step 4.

### 2. Setup Verifications

For each detected tech stack, ensure the following verification definitions exist globally. Use the naming convention `<Stack><Type>`:

**Naming convention:**
- Format/Lint: `DotnetFormat`, `NpmLint`, `PythonLint`, `RustClippy`, `GoFmt`
- Build: `DotnetBuild`, `NpmBuild`, `PythonBuild`, `RustBuild`, `GoBuild`
- Test: `DotnetTest`, `NpmTest`, `PythonTest`, `RustTest`, `GoTest`

**Example verification prompts by stack:**

| Stack | Verification | Prompt guidance |
|-------|-------------|-----------------|
| .NET | DotnetFormat | Run `dotnet format` scoped to changed .cs files, commit fixes if any |
| .NET | DotnetBuild | Run `dotnet build --warnaserror` in worktree, verify zero errors/warnings |
| .NET | DotnetTest | Run `dotnet test` with filter from plan's Tests section |
| JS/TS | NpmLint | Run `npm run lint` / `pnpm lint` (or equivalent) on changed files |
| JS/TS | NpmBuild | Run `npm run build` / `pnpm build` (or equivalent) and verify success |
| JS/TS | NpmTest | Run `npm test` / `pnpm test` with appropriate filter |
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
2. If not, create it with `tendril verification add <name> --prompt="<prompt>"`
3. Add it to the project with `tendril project add-verification <project-name> <name> --required`

Always add `CheckResult` as a verification (it exists in the default config):
```bash
tendril project add-verification <project-name> CheckResult --required
```

### 2.5. Ensure Correct Verification Order

Verifications run top-to-bottom during plan execution. The correct order is:

1. **Linting/Formatting** — e.g. `DotnetFormat`, `NpmLint`, `RustClippy`, `GoFmt`, `FrameworkFrontendLint`, `VitePlusCheck`
2. **Build** — e.g. `DotnetBuild`, `FrameworkDotnetBuild`, `NpmBuild`, `RustBuild`, `GoBuild`
3. **Tests** — e.g. `DotnetTest`, `NpmTest`, `RustTest`, `GoTest`
4. **CheckResult** — always last

After adding all verifications, verify ordering with `tendril project get <project-name>` and fix with:
```bash
tendril project move-verification <project-name> <name> --after=<other>
```

Use `--after` when adding verifications to place them correctly from the start:
```bash
tendril project add-verification <project-name> DotnetBuild --required --after=DotnetFormat
tendril project add-verification <project-name> DotnetTest --required --after=DotnetBuild
tendril project add-verification <project-name> CheckResult --required --after=DotnetTest
```

### 3. Setup Review Actions

Review actions make it easy to start the application from a worktree during code review.
To ensure the setup works out-of-the-box on fresh worktrees, review actions MUST automatically install dependencies before running the application (e.g. using `&&` to chain the installation and start commands).

Inspect each repo to determine how to run the application. For website projects, prefer commands that open the browser automatically:
- **.NET project** with a runnable entry point: `dotnet run --project Worktrees/<RepoName>/<path-to-project> --browse --find-available-port`
- **Vite+ (`vite-plus`) project**: `cd Worktrees/<RepoName>/<path-to-frontend> && vp install && vp dev`
- **Vite project**:
  - Under `pnpm`: `cd Worktrees/<RepoName>/<path-to-frontend> && pnpm install && pnpm run dev -- --open`
  - Under `npm`: `cd Worktrees/<RepoName>/<path-to-frontend> && npm install && npm run dev -- --open`
  - Under `yarn`: `cd Worktrees/<RepoName>/<path-to-frontend> && yarn install && yarn run dev -- --open`
- **Angular CLI**: `cd Worktrees/<RepoName>/<path> && npm install && ng serve --open` (adapt for `pnpm`/`yarn` if detected)
- **Other Node.js app** (no open support): `cd Worktrees/<RepoName>/<path> && npm install && npm run dev` (adapt package manager as detected)
- **Python app**: `cd Worktrees/<RepoName> && python -m pip install -r requirements.txt && python -m <module>` (or `flask run` / `uv run ...` / `poetry run ...` if detected)
- **Static docs**: `start Worktrees/<RepoName>/docs/index.html`

For each review action:
- **name**: Short descriptive name (e.g. "App", "Docs", "Frontend", "API")
- **condition**: A `Test-Path` expression that checks if the worktree path exists (e.g. `Test-Path "Worktrees/<RepoName>/src/<Project>"`)
- **command**: The command to launch the application

```bash
tendril project add-review-action <project-name> "<name>" \
  --command="<launch command>" \
  --condition="Test-Path \"Worktrees/<RepoName>/<path>\""
```

### 4. Set the Stack Hash

Derive a single-line **Stack Descriptor Hash (SDH)** from the `tendril project-analyzer` report(s) you gathered in Step 1 and persist it on the project. The SDH is a canonical, similarity-preserving signature of the project's significant layers, the frameworks that build each layer, and its test technology: same stack → identical hash, similar stacks → similar strings. **The hash contains no spaces anywhere.**

Map the analyzer YAML to the hash: each non-auxiliary, non-workspace-root `component` becomes a layer (use its dominant `language` and its `framework`/`orm`/`database`/`styling`/`testing`-category `technologies`); `infrastructure` feeds the `infra` segment; testing technologies across all components collapse into the trailing `test` segment. Ignore components flagged `isAuxiliary` and pure `isWorkspaceRoot` aggregators. (The analyzer already drops low-confidence and incidental signals, so everything in the report is fair game.)

**Grammar**
```
hash    = segment ( "|" segment )*
segment = role [ "(" lang ")" ] ":" token ( "+" token )*
role    = "fe" | "mobile" | "desktop" | "be" | "fs" | "lib" | "db" | "infra" | "test"
lang    = canonical language slug (omit for db/infra/test)
token   = canonical technology slug
```

**Rules**

1. **Fixed layer order** (omit absent layers): `fe, mobile, desktop, be, fs, lib, db, infra, test`. Use `fs` only when a single framework genuinely spans client+server (Rails, Laravel, Django MVC, Phoenix, Blazor Server, full-stack Next.js); otherwise split into `fe`+`be`. Use `lib` for a library/SDK/CLI repo with no app layer.
2. **Defining tech only** — cap each layer to these slots (fewer is fine); slot order = token order:
   - `fe`: ui-framework, meta-framework/builder, styling-system
   - `mobile`: framework — `desktop`: framework
   - `be`: web-framework, orm — `fs`: framework, orm
   - `lib`: (none — language only)
   - `db`: engines (most-central first) — `infra`: container/orchestration/iac — `test`: test frameworks
   Drop icon sets, validation, state, routers, component kits, loggers, monitoring, analytics, CI providers, hosting brands. Include a `db` token only when backed by a real driver dependency (`psycopg`, `pg`, `mongoose`, a redis client), a compose/infra service, or a confident detection — never from an env-var placeholder or a default cache-driver config alone.
3. **Token order:** by the slot order above (base/primary first), then alphabetical within a slot; `db` and `test` are alphabetical (no primary).
4. **Base before meta** — a meta-framework implies and is preceded by its base, both included: `react+next`, `react+remix`, `react+gatsby`, `vue+nuxt`, `svelte+sveltekit`, `solid+solidstart`, `reactnative+expo`.
5. **Normalize to canonical slugs:** lowercase, drop version, remove non-alphanumeric chars, then apply synonyms:
   - Frameworks: Next.js→`next`, Nuxt→`nuxt`, SvelteKit→`sveltekit`, ASP.NET Core→`aspnetcore`, Entity Framework Core→`efcore`, React Native→`reactnative`, Spring Boot→`spring`, Ruby on Rails→`rails`, Express→`express`, NestJS→`nestjs`, Tailwind CSS→`tailwind`.
   - Languages: TypeScript→`ts`, JavaScript→`js`, Python→`py`, Ruby→`rb`, Go→`go`, Rust→`rs`, Java→`java`, Kotlin→`kt`, C#→`cs`, PHP→`php`, Swift→`swift`, Dart→`dart`, Elixir→`ex`.
   - DB: PostgreSQL→`postgres`, MySQL→`mysql`, MongoDB→`mongodb`, SQL Server→`mssql`, Redis→`redis`, SQLite→`sqlite`.
6. **Test layer:** collect significant test frameworks across all layers into ONE trailing `test:` segment, alphabetical, deduped. Omit if none.
7. **Multiplicity & determinism:** one segment per role. Multiple frontends/backends → merge significant tokens (deduped, ordered by Rules 3–4). Never emit versions, counts, paths, or spaces. Same input → identical output.

**Reference examples**
```
fe(ts):react+next+tailwind|be(py):fastapi+sqlmodel|db:postgres|test:playwright+pytest
fe(ts):react+vite+tailwind|be(py):fastapi|db:postgres+redis|test:vitest
fs(py):django|db:postgres|test:pytest
fe(cs):blazor|be(cs):aspnetcore+efcore|db:mssql|test:xunit
be(go):gin+gorm|db:postgres
mobile(dart):flutter|db:firebase
lib(py)|test:pytest
```

Self-check before persisting: segments in Rule-1 order; absent layers omitted; only defining tokens, ordered by Rules 3–4 (base before meta); all slugs normalized; one alphabetical `test:` segment or none; **no spaces anywhere**. Then save it:
```bash
tendril project set <project-name> stackHash "<hash>"
```

### 5. Summary

Print a summary of what was configured:
- Verifications added
- Review actions added

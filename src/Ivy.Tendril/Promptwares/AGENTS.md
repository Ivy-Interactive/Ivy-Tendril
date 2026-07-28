# Promptwares Authoring Guidelines

Shipped promptwares are used by all Tendril customers across different teams, tech stacks, operating systems, and AI agents. Every Program.md must work for everyone out of the box.

## Rules

- **Stack-agnostic.** Never assume a specific language, framework, or package manager. Use project verifications and context (from the **Projects** firmware section) instead of hard-coded commands. When examples are needed, show multiple stacks as comments (e.g. `.NET`, `JavaScript`, `Python`, `Go`).
- **Platform-agnostic.** No Windows-only or Unix-only commands, paths, or assumptions. Write shell examples in portable bash. Say "local filesystem path" not "Windows path". Do not reference platform-specific tools (`subst`, `powershell.exe`, etc.).
- **Agent-agnostic.** Do not reference any specific AI coding agent, its tools, or its capabilities (e.g. "Edit tool", "Grep tool", "Read tool"). Use generic verbs: "edit the file", "search for", "read the file".
- **Project-agnostic.** No references to internal projects, private packages, internal URLs, proprietary class names, or customer-specific infrastructure. Examples should use generic names (`my-project`, `auth_service`, `user_controller`).
- **CLI over scripts.** Prefer `tendril` CLI commands over PowerShell/Bash scripts in Tools/. The agent's base permissions include `Bash(tendril*)`, making CLI commands always accessible.
- **Equals-form CLI options.** Always pass `tendril` option values as `--option=value` (e.g. `--initial-prompt="..."`), never space-separated (`--option value`). The CLI parser reads any token starting with `-` as an option name, so a value that begins with a dash (a markdown bullet `- ...`, a flag-like word `--watch`) is mis-parsed and the command fails. The equals form keeps the value glued to its option as a single token; shell quoting alone does not fix it. Also keep positional arguments (e.g. a plan `<title>`) from beginning with `-`.
- **Config over convention.** Anything that varies between customers belongs in configuration (injected via the firmware header or **Projects** section), not in Program.md. If you find yourself writing "if using X, do Y" for a customer-specific X, it should be a config option instead.
- **Keep it concise.** The limiting factor is a human reading the plan. Every sentence must earn its place.
- **Loopback only for servers.** When promptwares write tests, demo/sample applications, or mock servers that listen on a port, configure them to bind to loopback (127.0.0.1 or localhost) instead of all network interfaces (0.0.0.0). Binding to 0.0.0.0 requires elevated permissions or is blocked on some operating systems and sandboxed environments, leading to "listen EPERM" errors.

# Promptwares Authoring Guidelines

Shipped promptwares are used by all Tendril customers across different teams, tech stacks, operating systems, and AI agents. Every Program.md must work for everyone out of the box.

## Rules

- **Stack-agnostic.** Never assume a specific language, framework, or package manager. Use config.yaml verifications and project context instead of hard-coded commands. When examples are needed, show multiple stacks as comments (e.g. `.NET`, `JavaScript`, `Python`, `Go`).
- **Platform-agnostic.** No Windows-only or Unix-only commands, paths, or assumptions. Write shell examples in portable bash. Say "local filesystem path" not "Windows path". Do not reference platform-specific tools (`subst`, `powershell.exe`, etc.).
- **Agent-agnostic.** Do not reference any specific AI coding agent, its tools, or its capabilities (e.g. "Edit tool", "Grep tool", "Read tool"). Use generic verbs: "edit the file", "search for", "read the file".
- **Project-agnostic.** No references to internal projects, private packages, internal URLs, proprietary class names, or customer-specific infrastructure. Examples should use generic names (`my-project`, `auth_service`, `user_controller`).
- **Tool references.** Refer to shipped tools by name without file extension: `Tools/Apply-SyncStrategy`, not `Tools/Apply-SyncStrategy.ps1`. The runtime resolves the correct script format.
- **Config over convention.** Anything that varies between customers belongs in config.yaml or the firmware header, not in Program.md. If you find yourself writing "if using X, do Y" for a customer-specific X, it should be a config option instead.
- **Keep it concise.** The limiting factor is a human reading the plan. Every sentence must earn its place.

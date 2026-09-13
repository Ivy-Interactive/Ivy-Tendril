# Cursor Setup Guide for Tendril Skills

This guide explains how to install and configure Tendril Agent Skills in Cursor.

## 1. Quick Installation (Skills CLI)

Install Tendril skills into your Cursor project using the skills CLI:

```bash
# Project-level installation (installs into .cursor/skills/)
npx skills add ivy-interactive/ivy-tendril --agent cursor

# Global installation (across all Cursor workspaces)
npx skills add ivy-interactive/ivy-tendril --agent cursor -g
```

## 2. Directory Layout in Cursor

Cursor searches for skill definitions in the following locations:

- **Project-level**: `.cursor/skills/<skill-name>/SKILL.md`
- **Global / User-level**: `~/.cursor/skills/<skill-name>/SKILL.md` (macOS/Linux) or `%USERPROFILE%\.cursor\skills\<skill-name>\SKILL.md` (Windows)

Each folder contains:
- `SKILL.md`: Main instructions with YAML frontmatter
- Supporting reference documentation and scripts

## 3. Interaction with Cursor Rules (.cursorrules)

You can reference Tendril skills from your project's `.cursorrules` or `.cursor/rules/*.mdc` files:

```markdown
When debugging failed plans or reviewing changes:
- Reference skills/tendril-debug-plan for plan execution diagnosis.
- Run skills/tendril-review procedures before finalizing pull requests.
```

## 4. Usage in Cursor Agent Chat

In Cursor's agent chat window:
- Type `@tendril-debug-plan` or ask the agent to inspect a plan using its instructions.
- Ask Cursor to run `/tendril-review` on the active git diff.
- Run `/tendrillable` to rank issues for agent suitability.

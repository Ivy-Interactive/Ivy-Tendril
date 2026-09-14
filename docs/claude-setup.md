# Claude Code Setup Guide for Tendril Skills

This guide covers installing, configuring, and testing Tendril Agent Skills in Claude Code.

## 1. Installation via Plugin Marketplace

Tendril provides official plugin manifests at `.claude-plugin/marketplace.json` and `.claude-plugin/plugin.json`.

In Claude Code, add the Ivy Tendril repository as a marketplace source:

```
/plugin marketplace add ivy-interactive/ivy-tendril
```

Then install the `tendril-skills` plugin:

```
/plugin install tendril-skills@ivy-tendril
```

## 2. Local Development and Testing

When developing skills locally or testing changes before pushing:

Launch Claude Code with the plugin directory pointing to your local repository checkout:

```bash
claude --plugin-dir /path/to/ivy-tendril
```

Claude Code will read `.claude-plugin/plugin.json` and automatically mount all skills defined in `skills/`.

## 3. Backward Compatibility with .claude/skills

For local repository workflows within Ivy Tendril:
- Symlinks in `.claude/skills/<skill-name>` point to `../../skills/<skill-name>`.
- Any existing local Claude Code configuration referencing `.claude/skills/` continues to function seamlessly without manual reconfiguration.

## 4. Invoking Skills in Claude Code

Once installed, use slash commands directly in your Claude Code session:

- `/tendril-debug-plan <plan-id>`: Debug failed or slow plans.
- `/tendril-debug-job <job-id>`: Inspect job artifacts and agent decision logs.
- `/tendril-review`: Perform code quality and regression checks on current diffs.
- `/tendrillable <url>`: Classify backlog issues according to autonomous agent rubrics.
- `/tendril-release`: Automate version bumps, package updates, and release workflows.

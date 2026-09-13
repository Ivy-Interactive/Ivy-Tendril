# Google Antigravity Setup Guide for Tendril Skills

This guide covers installing and using Tendril skills with Google Antigravity CLI (`agy`) and Antigravity IDE.

## 1. Antigravity CLI Installation

Tendril provides an Antigravity plugin manifest at `.agents/plugins/marketplace.json`.

### Install from Remote Git Repository
```bash
agy plugin install https://github.com/ivy-interactive/ivy-tendril.git
```

### Install from Local Repository Checkout
During local development or within an Ivy Tendril checkout:
```bash
agy plugin install ./
```

## 2. Plugin Verification and Discovery

Verify that the plugin and its associated skills are loaded:

```bash
# List installed plugins
agy plugin list

# Verify available skills
agy skill list
```

You will see the bundled Tendril skills:
- `tendril-debug-plan`
- `tendril-debug-job`
- `tendril-review`
- `tendrillable`
- `tendril-release`
- `tendril-extension`

## 3. Skill Invocation in Antigravity

In any interactive Antigravity agent session or automated script:

- Ask Antigravity to debug a plan:
  ```
  Use tendril-debug-plan to investigate plan 00486
  ```
- Review pending worktree diffs:
  ```
  Run tendril-review on the current changes
  ```
- Triage candidate backlog issues:
  ```
  Run tendrillable on https://github.com/ivy-interactive/ivy-tendril 5
  ```

## 4. Antigravity IDE Integration

When working inside Antigravity IDE:
1. Skills placed in the root `.agents/skills/` directory of your workspace are automatically indexed.
2. To link the Ivy Tendril extension into Antigravity IDE:
   ```bash
   skills/tendril-extension/scripts/install-antigravity.sh
   ```
3. Reload Antigravity IDE (`Cmd+Shift+P` -> `Developer: Reload Window`).

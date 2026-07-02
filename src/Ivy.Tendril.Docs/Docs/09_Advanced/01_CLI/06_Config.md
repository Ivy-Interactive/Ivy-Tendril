---
searchHints:
  - config
  - configuration
  - settings
  - jobTimeout
  - codingAgent
  - planTemplate
---

# config

<Ingress>
Get and set top-level Tendril settings stored in `config.yaml` — the same values you can edit under Settings in the UI.
</Ingress>

## Commands

```terminal
>tendril config get <key>
>tendril config set <key> <value>
>tendril config set <key> --file <path>
>tendril config set <key> --stdin
```

- **get** — prints the raw value to stdout (no formatting), so it round-trips cleanly to a file
- **set** — writes the value and validates it before saving. Provide the value inline, or via `--file` / `--stdin` for long or multiline values

## Keys

| Key | Type | Bounds |
|-----|------|--------|
| `codingAgent` | string | — |
| `jobTimeout` | int (minutes) | 1–480 |
| `staleOutputTimeout` | int (minutes) | 1–60 |
| `gitTimeout` | int (minutes) | 1–30 |
| `maxConcurrentJobs` | int | 1–100 |
| `planTemplate` | string (may be multiline) | — |

Out-of-range or non-integer values are rejected before anything is written, so a bad value never clobbers the current one.

## Examples

```terminal
># Read a value
>tendril config get jobTimeout

># Set a scalar value
>tendril config set jobTimeout 60
>tendril config set codingAgent claude

># Set a long/multiline plan template from a file
>tendril config set planTemplate --file plan-template.md

># ...or from stdin
>cat plan-template.md | tendril config set planTemplate --stdin

># Round-trip the template back out
>tendril config get planTemplate > plan-template.md
```

<Callout type="Tip">
Use `--file` or `--stdin` for `planTemplate`: passing a long, multiline value inline is fragile in the shell, and values that begin with `-` confuse argument parsing.

</Callout>

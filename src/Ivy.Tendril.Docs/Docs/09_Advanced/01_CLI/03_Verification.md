---
searchHints:
  - verification
  - verify
  - check
  - prompt
  - definition
---

# verification

<Ingress>
Manage global verification definitions stored in `config.yaml`. These can be referenced by projects and plans.
</Ingress>

## Commands

```terminal
>tendril verification list
>tendril verification get <name>
>tendril verification add <name> [--prompt <text>]
>tendril verification remove <name>
>tendril verification set <name> <field> <value>
```

- **list** — shows all definitions with a preview of the prompt
- **get** — prints the full prompt to stdout
- **add** — reads prompt from stdin if `--prompt` is omitted
- **set** — supported fields: `name`, `prompt`

## Examples

```terminal
># Add with inline prompt
>tendril verification add BuildPasses --prompt "Run dotnet build and confirm exit code 0"

># Add from file
>cat prompt.md | tendril verification add BuildPasses

># Update the prompt
>tendril verification set BuildPasses prompt "Run dotnet build --no-restore and check for errors"

># List all definitions
>tendril verification list

># View full prompt
>tendril verification get BuildPasses
```

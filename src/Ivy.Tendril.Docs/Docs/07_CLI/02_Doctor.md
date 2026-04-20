---
icon: Stethoscope
searchHints:
  - doctor
  - health
  - diagnostics
---

<Text Color="Green" Small Bold>CLI</Text>

# doctor

<Ingress>
Run system diagnostics from the command line.
</Ingress>

## Usage

```bash
tendril doctor
```

Validates your Tendril installation:

| Check | Details |
|-------|---------|
| `TENDRIL_HOME` | Environment variable is set and directory exists |
| `config.yaml` | Configuration file is present and parseable |
| Required software | `gh` (authenticated), `git` |
| Optional software | `pandoc` |
| PowerShell | `pwsh` is available |
| Database | Schema is intact and current |
| Agent models | Configured coding agent is reachable |

<Callout type="Tip">
To check plan health specifically, use `tendril plan doctor`. See the [Plan](03_Plan.md) page for details.

</Callout>

---
searchHints:
  - install
  - setup
  - prerequisites
  - getting-started
  - macos
  - windows
icon: Download
---

# Installation

<Ingress>
Install Tendril on macOS, Linux, or Windows using one of the quick install scripts.
</Ingress>

```csharp demo
public class InstallationTabs : ViewBase
{
    public override object? Build()
    {
        return Layout.Tabs(
            new Tab("macOS / Linux", Layout.Vertical()
                | new CodeBlock("curl -sSf https://cdn.ivy.app/install-tendril.sh | sh", Languages.Bash)
                | new Callout("This script downloads the standalone installer package (pkg) on macOS or the standalone AppImage on Linux and configures the environment.", icon: Icons.Info)
            ),
            new Tab("Windows", Layout.Vertical()
                | new CodeBlock("irm https://cdn.ivy.app/install-tendril.ps1 | iex", Languages.Powershell)
                | new Callout("This script downloads and executes the standalone Windows setup installer.", icon: Icons.Info)
            )
        ).Variant(TabsVariant.Content);
    }
}
```

## Run

```bash
tendril
```

<Callout type="warning">
Before starting the onboarding wizard, please make sure your preferred coding agent is installed and fully authorized on your machine.
</Callout>

The first time you run Tendril, you'll be guided through an onboarding wizard. During setup, Tendril will create a configuration file at `$TENDRIL_HOME/config.yaml` (default: `~/.tendril/config.yaml`).

## Update

Tendril features automatic background updates on desktop. You can also rerun the installation scripts at any time to update to the latest release version.

<style>
  article h2 {
    border-bottom: none !important;
    padding-bottom: 0 !important;
  }
  footer {
    border-top: none !important;
  }
</style>
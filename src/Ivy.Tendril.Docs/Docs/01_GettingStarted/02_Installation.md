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
Install Tendril on macOS, Linux, or Windows using one of the methods below.
</Ingress>

```csharp demo
public class InstallationTabs : ViewBase
{
    public override object? Build()
    {
        return Layout.Tabs(
            new Tab("macOS / Linux", Layout.Vertical().Padding(8, 0)
                | Text.P("One-liner: installs Tendril and required backend tools.")
                | new CodeBlock("curl -sSf https://cdn.ivy.app/install-tendril.sh | sh", Languages.Bash)
            ),
            new Tab("Windows", Layout.Vertical().Padding(8, 0)
                | Text.P("One-liner: installs Tendril and required backend tools.")
                | new CodeBlock("irm https://cdn.ivy.app/install-tendril.ps1 | iex", Languages.Powershell)
            ),
            new Tab(".NET Tool", Layout.Vertical().Padding(8, 0)
                | Text.P("Global install from NuGet:")
                | new CodeBlock("dotnet tool install -g Ivy.Tendril", Languages.Bash)
                | new Callout("Powershell 7, Git and gh CLI need to be present on your machine if you install using `dotnet tool` command", icon: Icons.Info)
                | new Callout("On macOS/Linux, if you've never used .NET tools before, you may need to add the global tool directory to your PATH. Add `export PATH=\"$PATH:$HOME/.dotnet/tools\"` to your `~/.zshrc` or `~/.bashrc` to run `tendril` directly from your terminal. This is done automatically with the quick install commands above.", icon: Icons.Info)
            )
        ).Variant(TabsVariant.Content);
    }
}
```

## Run

```bash
tendril
```

The first time you run Tendril, you'll be guided through an onboarding wizard. During setup, Tendril will create a configuration file at `$TENDRIL_HOME/config.yaml` (default: `~/.tendril/config.yaml`).

## Update

You can update Ivy Tendril at anytime after the initial install using the dotnet tool update command:

```bash
dotnet tool update -g Ivy.Tendril
```
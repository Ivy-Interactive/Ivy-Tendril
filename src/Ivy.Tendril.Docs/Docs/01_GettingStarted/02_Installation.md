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

## Quick Install

One-liner: installs Tendril and required backend tools.

### macOS / Linux

```bash
curl -sSf https://cdn.ivy.app/install-tendril.sh | sh
```

### Windows

```powershell
Invoke-RestMethod -Uri https://cdn.ivy.app/install-tendril.ps1 | Invoke-Expression
```

## .NET Tool

Global install from NuGet:

```bash
dotnet tool install -g Ivy.Tendril
```

<Callout type="Tip">
Powershell 7, Git and gh CLI need to be present on your machine if you install using `dotnet tool` command
</Callout>

<Callout type="Info">
On macOS/Linux, if you've never used .NET tools before, you may need to add the global tool directory to your PATH. Add `export PATH="$PATH:$HOME/.dotnet/tools"` to your `~/.zshrc` or `~/.bashrc` to run `tendril` directly from your terminal. This is done automatically with the quick install commands above.
</Callout>

## Run

```bash
tendril
```

## Update

You can update Ivy Tendril at anytime after the initial install using the dotnet tool update command:

```bash
dotnet tool update -g Ivy.Tendril
```
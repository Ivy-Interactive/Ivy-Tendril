---
name: tendril-release
description: Updates the used Ivy NuGet packages to the latest stable versions in a branch from development, builds/verifies, merges into development, creates a PR into main, merges it, merges main back to development, and triggers the GitHub Actions release workflow.
---

# Tendril Release Automator

This skill automates the release preparation and deployment process for Ivy Tendril. It manages package updates, local branch integration, GitHub PR generation and merging, branch synchronization, and triggers the final release workflow.

## Invocation

```bash
/tendril-release
```

## What This Skill Does

1. **Creates a release branch** off `development`.
2. **Updates Ivy NuGet packages** to their latest stable version available on NuGet.
3. **Builds the project** to verify compilation and package compatibility.
4. **Commits and merges** the changes back into `development`.
5. **Increments the patch version** in `Directory.Build.props` (e.g. `1.0.35` -> `1.0.36`).
6. **Creates a Pull Request** from `development` into `main`.
7. **Merges the PR** into `main` (if mergeable).
8. **Synchronizes the branches** by merging `main` back into `development`.
9. **Triggers the GitHub release workflow** (`publish-tendril.yml`) on `main`.

## Prerequisites

- **GitHub CLI** (`gh`) must be installed and authenticated with PR and workflow write access.
- **PowerShell 7** (`pwsh`) must be installed on the system.

---

## Step-by-Step Workflow

### Phase 1 — Create Release Prep Branch
Ensure the workspace is clean and up to date, then check out a temporary package update branch from `development`:
```bash
git checkout development
git pull origin development
git checkout -b release/update-packages
```

### Phase 2 — Update Ivy packages
Run the PowerShell update script. This script temporarily disables local `IvySource` references to allow NuGet resolution, finds all package references starting with `Ivy` or `Ivy.*` in all project files, runs `dotnet add` to update them to their latest versions, and restores the original settings when finished:
```bash
pwsh src/.releases/UpdateIvyPackages.ps1
```

### Phase 3 — Verify Build
Verify that the package updates do not break compilation. Run builds with `IvySource` set to `false` to verify NuGet package resolution:
```bash
dotnet build src/Ivy.Tendril/Ivy.Tendril.csproj /p:IvySource=false
dotnet build src/Ivy.Tendril.Updater/Ivy.Tendril.Updater.csproj /p:IvySource=false
```

If the build fails, abort the process and notify the developer. Do not proceed to commit.

### Phase 4 — Merge to Development and Increment Version
If the build succeeds, commit the changes and merge the branch back into `development`:
```bash
git add .
git commit -m "chore: update Ivy NuGet package dependencies to latest stable"
git push origin release/update-packages

# Switch to development and merge
git checkout development
git merge release/update-packages --no-ff -m "Merge branch 'release/update-packages' into development"
git push origin development

# Clean up local and remote release branch
git branch -d release/update-packages
git push origin --delete release/update-packages

# Increment the patch version in Directory.Build.props and commit
pwsh src/.releases/IncrementVersion.ps1
git add src/Directory.Build.props
git commit -m "chore: bump patch version for release"
git push origin development
```

### Phase 5 — Create and Merge PR into Main
Generate a Pull Request to merge the updated `development` branch into `main`:
```bash
gh pr create --base main --head development --title "Release: Merge development into main" --body "Automated release PR created by Tendril Release Skill."
```

Once the PR is created, wait for/trigger the merge:
- **If checks are required or you want to queue it**:
  ```bash
  gh pr merge --merge --auto
  ```
- **If you can merge immediately**:
  ```bash
  gh pr merge --merge
  ```

### Phase 6 — Sync Main Back into Development
Keep the branches perfectly in sync by merging `main` back into `development` after the PR is merged:
```bash
git checkout main
git pull origin main
git checkout development
git merge main --no-ff -m "Merge branch 'main' into development to sync"
git push origin development
```

### Phase 7 — Trigger Release Workflow
Trigger the release Action workflow (`publish-tendril.yml`) on `main`:
```bash
gh workflow run publish-tendril.yml --ref main
```

Confirm that the workflow has been dispatched by showing the URL/logs:
```bash
gh run list --workflow=publish-tendril.yml --limit 1
```

---

## Troubleshooting & Common Mistakes

- **Authentication Errors**: If `gh` is not authenticated, authenticate by running `gh auth login`.
- **Merge Conflicts**: If merging `main` back into `development` or `development` into `main` has conflicts, stop and notify the user to resolve them manually.
- **Untracked nuget.config**: Do not leave any temporary `nuget.config` files in the repository.

<#
.SYNOPSIS
    Applies sync strategy (fetch/rebase/merge) to a worktree after creation.

.DESCRIPTION
    Reads sync strategy from RepoConfigs firmware value and applies the appropriate
    git operations to keep the worktree branch in sync with the base branch.

.PARAMETER WorktreePath
    Absolute path to the worktree directory.

.PARAMETER SyncStrategy
    Sync strategy to apply: "fetch" (default), "rebase", or "merge".

.PARAMETER BaseBranch
    Base branch name to sync with (e.g., "main", "development").

.EXAMPLE
    Apply-SyncStrategy.ps1 -WorktreePath "D:\Tendril\Plans\03294-Test\worktrees\Ivy-Framework" -SyncStrategy "rebase" -BaseBranch "development"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$WorktreePath,

    [Parameter(Mandatory = $true)]
    [string]$SyncStrategy,

    [Parameter(Mandatory = $true)]
    [string]$BaseBranch
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $WorktreePath)) {
    Write-Error "Worktree path does not exist: $WorktreePath"
    exit 1
}

Push-Location $WorktreePath
try {
    Write-Host "Applying sync strategy '$SyncStrategy' for worktree: $WorktreePath" -ForegroundColor Cyan

    switch ($SyncStrategy.ToLower()) {
        "fetch" {
            # Already fetched during worktree creation - no additional action needed
            Write-Host "Sync strategy 'fetch': No additional action needed (already fetched during worktree creation)" -ForegroundColor Green
        }

        "rebase" {
            Write-Host "Fetching latest from origin..." -ForegroundColor Gray
            git fetch origin
            if ($LASTEXITCODE -ne 0) {
                Write-Error "git fetch failed with exit code $LASTEXITCODE"
                exit $LASTEXITCODE
            }

            Write-Host "Rebasing onto origin/$BaseBranch..." -ForegroundColor Gray
            git rebase "origin/$BaseBranch"
            if ($LASTEXITCODE -ne 0) {
                Write-Error "git rebase failed with exit code $LASTEXITCODE. Worktree may have uncommitted changes or conflicts."
                exit $LASTEXITCODE
            }

            Write-Host "Rebase completed successfully" -ForegroundColor Green
        }

        "merge" {
            Write-Host "Fetching latest from origin..." -ForegroundColor Gray
            git fetch origin
            if ($LASTEXITCODE -ne 0) {
                Write-Error "git fetch failed with exit code $LASTEXITCODE"
                exit $LASTEXITCODE
            }

            Write-Host "Merging origin/$BaseBranch..." -ForegroundColor Gray
            git merge "origin/$BaseBranch" --no-edit
            if ($LASTEXITCODE -ne 0) {
                Write-Error "git merge failed with exit code $LASTEXITCODE. Worktree may have uncommitted changes or conflicts."
                exit $LASTEXITCODE
            }

            Write-Host "Merge completed successfully" -ForegroundColor Green
        }

        default {
            Write-Warning "Unknown sync strategy '$SyncStrategy' - defaulting to 'fetch' (no action)"
        }
    }
}
finally {
    Pop-Location
}

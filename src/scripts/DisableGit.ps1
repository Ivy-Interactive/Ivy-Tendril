<#
.SYNOPSIS
Temporarily break git authentication on a repo to reproduce the ExecutePlan
worktree-fetch failure, then restore it.

.DESCRIPTION
Makes `git fetch origin` fail with `fatal: Authentication failed` on demand.

Because the Ivy-Tendril remote is a *public* repo, anonymous fetch normally
succeeds regardless of credentials, so a bad token alone does not break it.
Instead this script:
  * points 'origin' at a non-existent repo path (adds an '-authfail' suffix),
    which forces GitHub to demand authentication, and
  * sets repo-local credential config (helper='', interactive=never,
    core.askpass=echo) so that forced auth challenge fails non-interactively
    -- no Credential Manager dialog, no hang, deterministic exit 128.

The original 'origin' URL is stored in git config
(tendril.disable-git.original-url) and everything is restored with -Revert.

.PARAMETER RepoPath
Target repository (required).

.PARAMETER Revert
Restore the original 'origin' URL and clear all state this script set.

.EXAMPLE
./DisableGit.ps1 D:\Repos\Foo          # break auth in that repo
./DisableGit.ps1 D:\Repos\Foo -Revert  # restore
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$RepoPath,
    [switch]$Revert
)

$ErrorActionPreference = "Stop"
# git returns non-zero for benign cases (config --get / --unset on a missing
# key); don't let those become terminating errors when run via `pwsh -File`.
$PSNativeCommandUseErrorActionPreference = $false

$ConfigKey = "tendril.disable-git.original-url"

# Named 'Invoke-Git', NOT 'Git': PowerShell command lookup is case-insensitive
# and functions outrank executables, so a function named 'Git' would shadow
# git.exe and every `& git` call would recurse into itself.
function Invoke-Git { param([string[]]$GitArgs) & git -C $repoRoot @GitArgs }

# Resolve repo root
$repoRoot = (& git -C $RepoPath rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    Write-Host "Error: '$RepoPath' is not inside a git repository." -ForegroundColor Red
    exit 1
}
$repoRoot = $repoRoot.Trim()
Write-Host "Repo: $repoRoot" -ForegroundColor Blue

$saved = (Invoke-Git @("config", "--local", "--get", $ConfigKey) 2>$null)

if ($Revert) {
    if (-not $saved) {
        Write-Host "Nothing to revert: git auth was not disabled by this script." -ForegroundColor Yellow
        exit 0
    }
    Invoke-Git @("remote", "set-url", "origin", $saved) | Out-Null
    Invoke-Git @("config", "--local", "--unset", "credential.helper") 2>$null | Out-Null
    Invoke-Git @("config", "--local", "--unset", "credential.interactive") 2>$null | Out-Null
    Invoke-Git @("config", "--local", "--unset", "core.askpass") 2>$null | Out-Null
    Invoke-Git @("config", "--local", "--unset", $ConfigKey) 2>$null | Out-Null
    Write-Host "Restored 'origin' -> $saved" -ForegroundColor Green
    Write-Host "Git authentication re-enabled." -ForegroundColor Green
    exit 0
}

# --- Disable path ---
if ($saved) {
    Write-Host "Already disabled (saved original: $saved). Run with -Revert to restore first." -ForegroundColor Yellow
    exit 1
}

$current = (Invoke-Git @("remote", "get-url", "origin") 2>$null)
if (-not $current) {
    Write-Host "Error: repo has no 'origin' remote." -ForegroundColor Red
    exit 1
}
$current = $current.Trim()
if ($current -notmatch '^https://.*github\.com/') {
    Write-Host "Error: 'origin' is not an HTTPS github.com URL ($current). This script only handles HTTPS remotes." -ForegroundColor Red
    exit 1
}

# Point origin at a non-existent repo so GitHub forces an auth challenge even
# though the real repo is public.
if ($current -match '\.git$') {
    $broken = $current -replace '\.git$', '-authfail.git'
} else {
    $broken = "$current-authfail"
}

Invoke-Git @("config", "--local", $ConfigKey, $current) | Out-Null
Invoke-Git @("remote", "set-url", "origin", $broken) | Out-Null
# Make the forced auth challenge fail non-interactively.
Invoke-Git @("config", "--local", "credential.helper", "") | Out-Null
Invoke-Git @("config", "--local", "credential.interactive", "never") | Out-Null
Invoke-Git @("config", "--local", "core.askpass", "echo") | Out-Null

Write-Host "Disabled git auth for 'origin':" -ForegroundColor Green
Write-Host "  was: $current"
Write-Host "  now: $broken"
Write-Host "`n``git fetch origin`` will now fail with 'Authentication failed' (exit 128)." -ForegroundColor Yellow
Write-Host "Run './DisableGit.ps1 $RepoPath -Revert' to restore." -ForegroundColor Yellow

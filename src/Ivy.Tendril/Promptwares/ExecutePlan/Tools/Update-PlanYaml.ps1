<#
.SYNOPSIS
    Updates plan.yaml file safely when the tendril CLI is unavailable.

.DESCRIPTION
    Provides atomic updates to plan.yaml using text manipulation. This is a fallback
    for when the tendril CLI fails due to missing service dependencies.

.PARAMETER PlanFolder
    Path to the plan folder containing plan.yaml

.PARAMETER AddCommit
    Commit hash to add to the commits list

.PARAMETER SetVerification
    Verification name and status (format: "Name=Status")

.EXAMPLE
    Update-PlanYaml.ps1 -PlanFolder "D:\Plans\03536-Test" -AddCommit "abc1234"
    Update-PlanYaml.ps1 -PlanFolder "D:\Plans\03536-Test" -SetVerification "DotnetBuild=Pending"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$PlanFolder,

    [Parameter(Mandatory = $false)]
    [string]$AddCommit,

    [Parameter(Mandatory = $false)]
    [string]$SetVerification
)

$ErrorActionPreference = "Stop"

$planYamlPath = Join-Path $PlanFolder "plan.yaml"

if (-not (Test-Path $planYamlPath)) {
    Write-Error "plan.yaml not found at: $planYamlPath"
    exit 1
}

# Read current YAML as lines
$lines = Get-Content -Path $planYamlPath

# Apply updates
if ($AddCommit) {
    # Find commits: line and add the commit after it
    $commitsIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^commits:\s*$') {
            $commitsIndex = $i
            break
        }
    }

    if ($commitsIndex -eq -1) {
        Write-Error "Could not find 'commits:' in plan.yaml"
        exit 1
    }

    # Check if commit already exists
    $commitExists = $false
    for ($i = $commitsIndex + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "^- $AddCommit") {
            $commitExists = $true
            break
        }
        if ($lines[$i] -notmatch '^\s*-\s+\w+' -and $lines[$i] -notmatch '^\s*$') {
            break
        }
    }

    if (-not $commitExists) {
        # Insert after commits: line
        $newLines = @()
        $newLines += $lines[0..$commitsIndex]
        $newLines += "- $AddCommit"
        $newLines += $lines[($commitsIndex + 1)..($lines.Count - 1)]
        $lines = $newLines
        Write-Host "Added commit: $AddCommit" -ForegroundColor Green
    } else {
        Write-Host "Commit already exists: $AddCommit" -ForegroundColor Yellow
    }
}

if ($SetVerification) {
    $parts = $SetVerification -split "=", 2
    $name = $parts[0]
    $status = $parts[1]

    # Block self-certification of delegated verifications (those that have their own promptware)
    if ($status -eq "Pass") {
        $promptsRoots = @()
        # From script location: Tools/ -> ExecutePlan/ -> Promptwares/
        $promptsRoots += Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        # From TENDRIL_HOME
        if ($env:TENDRIL_HOME) {
            $promptsRoots += Join-Path $env:TENDRIL_HOME "Promptwares"
        }
        foreach ($root in $promptsRoots) {
            $promptwareDir = Join-Path $root $name
            if (Test-Path $promptwareDir) {
                Write-Error "BLOCKED: '$name' is a delegated verification (has its own promptware at $promptwareDir). It must be run via 'tendril promptware $name' — ExecutePlan cannot self-certify it as Pass. Set it to Fail if the CLI is unavailable."
                exit 1
            }
        }
    }

    # Find the verification entry and update its status
    $updated = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*-\s+name:\s+(.+)$') {
            $verName = $matches[1]
            if ($verName -eq $name) {
                # Next line should be status with proper indentation
                if ($i + 1 -lt $lines.Count -and $lines[$i + 1] -match '^\s+status:\s+') {
                    $lines[$i + 1] = $lines[$i + 1] -replace '(^\s+status:\s+)\w+', "`${1}$status"
                    $updated = $true
                    Write-Host "Set verification $name = $status" -ForegroundColor Green
                    break
                }
            }
        }
    }

    if (-not $updated) {
        Write-Warning "Verification '$name' not found or could not be updated"
    }
}

# Update timestamp
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^updated:\s+') {
        $lines[$i] = "updated: $timestamp"
        break
    }
}

# Write atomically
$tempPath = "$planYamlPath.tmp"
$lines | Out-File -FilePath $tempPath -Encoding utf8
Move-Item -Path $tempPath -Destination $planYamlPath -Force

Write-Host "Updated plan.yaml successfully" -ForegroundColor Cyan

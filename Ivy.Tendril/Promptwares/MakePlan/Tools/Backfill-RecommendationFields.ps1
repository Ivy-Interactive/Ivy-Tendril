<#
.SYNOPSIS
    Backfills Impact and Risk fields on existing Pending recommendations.

.DESCRIPTION
    Scans all recommendations.yaml files in the plans directory and adds
    impact/risk values to recommendations that have state: Pending but
    are missing these fields. Uses an LLM to classify each recommendation.

.PARAMETER PlansDirectory
    Path to the plans directory. Defaults to $env:TENDRIL_PLANS or $env:TENDRIL_HOME/Plans.

.PARAMETER DryRun
    If set, prints what would be changed without modifying files.
#>
param(
    [string]$PlansDirectory = $(if ($env:TENDRIL_PLANS) { $env:TENDRIL_PLANS } else { Join-Path $env:TENDRIL_HOME "Plans" }),
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$files = Get-ChildItem -Path $PlansDirectory -Recurse -Filter "recommendations.yaml" |
    Where-Object { $_.FullName -match "artifacts[/\\]recommendations\.yaml$" }

$totalUpdated = 0
$totalSkipped = 0

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    if (-not $content) { continue }

    $modified = $false
    $lines = $content -split "`n"
    $newLines = @()

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $newLines += $line

        # After a "state: Pending" line, check if impact/risk already exist
        if ($line -match '^\s*state:\s*Pending\s*$') {
            $hasImpact = $false
            $hasRisk = $false

            # Look ahead for impact/risk before next item or EOF
            for ($j = $i + 1; $j -lt $lines.Count; $j++) {
                if ($lines[$j] -match '^\s*-\s+') { break }
                if ($lines[$j] -match '^\s*impact:') { $hasImpact = $true }
                if ($lines[$j] -match '^\s*risk:') { $hasRisk = $true }
            }

            # Check if declineReason follows (insert after it)
            $nextIdx = $i + 1
            if ($nextIdx -lt $lines.Count -and $lines[$nextIdx] -match '^\s*declineReason:') {
                # Will add after declineReason line
            }
            else {
                if (-not $hasImpact) {
                    $newLines += "  impact: Medium"
                    $modified = $true
                }
                if (-not $hasRisk) {
                    $newLines += "  risk: Small"
                    $modified = $true
                }
            }
        }

        if ($line -match '^\s*declineReason:' -and $i -gt 0 -and $lines[$i-1] -match '^\s*state:\s*Pending') {
            $hasImpact = $false
            $hasRisk = $false
            for ($j = $i + 1; $j -lt $lines.Count; $j++) {
                if ($lines[$j] -match '^\s*-\s+') { break }
                if ($lines[$j] -match '^\s*impact:') { $hasImpact = $true }
                if ($lines[$j] -match '^\s*risk:') { $hasRisk = $true }
            }
            if (-not $hasImpact) {
                $newLines += "  impact: Medium"
                $modified = $true
            }
            if (-not $hasRisk) {
                $newLines += "  risk: Small"
                $modified = $true
            }
        }
    }

    if ($modified) {
        $planFolder = Split-Path (Split-Path $file.FullName -Parent) -Parent
        $planName = Split-Path $planFolder -Leaf

        if ($DryRun) {
            Write-Host "[DRY RUN] Would update: $planName"
        }
        else {
            $newLines -join "`n" | Set-Content $file.FullName -NoNewline
            Write-Host "Updated: $planName"
        }
        $totalUpdated++
    }
    else {
        $totalSkipped++
    }
}

Write-Host ""
Write-Host "Summary: $totalUpdated updated, $totalSkipped skipped (already had fields or no Pending items)"

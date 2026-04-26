param(
    [Parameter(Mandatory=$true)]
    [string]$PlanFolder,

    [Parameter(Mandatory=$false)]
    [string]$AddCommit,

    [Parameter(Mandatory=$false)]
    [string]$SetVerification,

    [Parameter(Mandatory=$false)]
    [string]$VerificationStatus
)

$ErrorActionPreference = "Stop"

$planYamlPath = Join-Path $PlanFolder "plan.yaml"

if (-not (Test-Path $planYamlPath)) {
    Write-Error "plan.yaml not found at $planYamlPath"
    exit 1
}

# Read the YAML content
$content = Get-Content $planYamlPath -Raw

# Parse YAML manually (simple key-value parsing)
$lines = $content -split "`n"

if ($AddCommit) {
    # Add commit to commits list
    $newLines = @()
    $inCommits = $false
    $commitsAdded = $false

    foreach ($line in $lines) {
        if ($line -match "^commits:") {
            $inCommits = $true
            $newLines += $line
            # Check if commits list is empty (next line is not indented or is another key)
            continue
        } elseif ($inCommits -and $line -match "^[a-zA-Z]") {
            # End of commits section
            if (-not $commitsAdded) {
                $newLines += "- $AddCommit"
                $commitsAdded = $true
            }
            $inCommits = $false
            $newLines += $line
        } elseif ($inCommits) {
            # Still in commits section
            $newLines += $line
            if ($line -match "^- ") {
                # There are existing commits, add ours
                if (-not $commitsAdded) {
                    $newLines += "- $AddCommit"
                    $commitsAdded = $true
                    $inCommits = $false
                }
            }
        } else {
            $newLines += $line
        }
    }

    # If we never found a commits section or it was at the end
    if ($inCommits -and -not $commitsAdded) {
        $newLines += "- $AddCommit"
    }

    $lines = $newLines
}

if ($SetVerification -and $VerificationStatus) {
    # Update verification status
    $newLines = @()
    $inVerifications = $false
    $updated = $false

    foreach ($line in $lines) {
        if ($line -match "^verifications:") {
            $inVerifications = $true
            $newLines += $line
        } elseif ($inVerifications -and $line -match "^[a-zA-Z]") {
            $inVerifications = $false
            $newLines += $line
        } elseif ($inVerifications -and $line -match "^\s*- name: $SetVerification") {
            $newLines += $line
            $updated = $true
        } elseif ($updated -and $line -match "^\s*status:") {
            $newLines += "  status: $VerificationStatus"
            $updated = $false
        } else {
            $newLines += $line
        }
    }

    $lines = $newLines
}

# Write back
$content = $lines -join "`n"
Set-Content -Path $planYamlPath -Value $content -NoNewline

Write-Output "Updated plan.yaml successfully"

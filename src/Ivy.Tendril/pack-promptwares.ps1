param(
    [Parameter(Mandatory=$true)]
    [string]$IntermediateOutputPath
)

$src = "Promptwares"
$staging = Join-Path $IntermediateOutputPath "promptwares-staging"
$zip = Join-Path $IntermediateOutputPath "promptwares.zip"

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
Copy-Item $src $staging -Recurse -Force

Get-ChildItem $staging -Recurse -Directory | Where-Object { $_.Name -in 'Logs','Memory' } | Remove-Item -Recurse -Force

# Remove tools that are NOT explicitly shipped.
# Only tools listed in $shippedTools are included in the package.
# All others (debugging, ops, migration scripts) are stripped.
$shippedTools = @{
    'CreatePlan'  = @('Validate-CodeAssertion.ps1', 'Find-DuplicatePlans.ps1', 'Find-ActivePlans.ps1')
    'CreatePr'    = @('Remove-PlanWorktree.ps1')
    'ExecutePlan' = @('Apply-SyncStrategy.ps1', 'Cleanup-Worktrees.ps1', 'Log-WorktreeEvent.ps1')
    'ExpandPlan'  = @()
    'SplitPlan'   = @()
    'UpdatePlan'  = @()
    'CreateIssue' = @()
}

foreach ($pw in $shippedTools.Keys) {
    $toolsDir = Join-Path $staging $pw "Tools"
    if (-not (Test-Path $toolsDir)) { continue }

    $allowed = $shippedTools[$pw]
    if ($allowed.Count -eq 0) {
        Remove-Item $toolsDir -Recurse -Force
        continue
    }

    Get-ChildItem $toolsDir -File | Where-Object { $_.Name -notin $allowed } | Remove-Item -Force
}

if (Test-Path $zip) { Remove-Item $zip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip)

Remove-Item $staging -Recurse -Force

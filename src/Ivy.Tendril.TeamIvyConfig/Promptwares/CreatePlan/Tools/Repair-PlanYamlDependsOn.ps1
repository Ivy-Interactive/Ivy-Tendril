<#
.SYNOPSIS
    Repairs plan.yaml files where 'priority' was incorrectly nested inside 'dependsOn'.

.DESCRIPTION
    Scans all plan.yaml files in the plans directory and fixes three known patterns:
      Pattern 1: dependsOn:\n  priority: N        (mapping under dependsOn)
      Pattern 2: dependsOn:\n  'priority: N'      (quoted string under dependsOn)
      Pattern 3: dependsOn:\n  - item\n  priority: N  (mixed list + mapping)

.PARAMETER PlansDirectory
    Path to the plans directory containing plan folders.

.PARAMETER DryRun
    If set, reports what would be changed without modifying files.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PlansDirectory,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$fixed = 0
$skipped = 0
$errors = 0

$planFolders = Get-ChildItem -Path $PlansDirectory -Directory | Where-Object { $_.Name -match '^\d{5}-' }

foreach ($folder in $planFolders) {
    $yamlPath = Join-Path $folder.FullName "plan.yaml"
    if (-not (Test-Path $yamlPath)) { continue }

    $content = Get-Content $yamlPath -Raw
    if (-not $content) { continue }

    try {
        # Parse YAML structure
        $yaml = $content | ConvertFrom-Yaml -Ordered

        # Check if dependsOn has a bug
        $hasBug = $false
        $priorityValue = 0

        if ($yaml.dependsOn -is [System.Collections.IDictionary]) {
            # dependsOn is a mapping instead of a sequence
            if ($yaml.dependsOn.ContainsKey('priority')) {
                $hasBug = $true
                $priorityValue = [int]$yaml.dependsOn['priority']
                $yaml.dependsOn.Remove('priority')

                # If dependsOn now only had priority, convert to empty array
                if ($yaml.dependsOn.Count -eq 0) {
                    $yaml['dependsOn'] = @()
                } else {
                    # Convert remaining keys to array (shouldn't happen in practice, but handle it)
                    $items = @($yaml.dependsOn.Keys | ForEach-Object { $yaml.dependsOn[$_] })
                    $yaml['dependsOn'] = $items
                }
            }
        } elseif ($yaml.dependsOn -is [System.Collections.IList]) {
            # dependsOn is a list, but might have 'priority: N' string item
            $priorityItem = $yaml.dependsOn | Where-Object { $_ -match '^priority:\s*(\d+)$' } | Select-Object -First 1
            if ($priorityItem) {
                $hasBug = $true
                if ($priorityItem -match 'priority:\s*(\d+)') {
                    $priorityValue = [int]$Matches[1]
                }
                # Remove the priority item from the list
                $yaml['dependsOn'] = @($yaml.dependsOn | Where-Object { $_ -notmatch '^priority:' })
            }
        }

        if (-not $hasBug) {
            $skipped++
            continue
        }

        # Ensure priority exists at root level
        $yaml['priority'] = $priorityValue

        # Serialize back to YAML
        $content = ConvertTo-Yaml $yaml

        if ($DryRun) {
            Write-Host "  WOULD FIX: $($folder.Name) (priority: $priorityValue)" -ForegroundColor Cyan
        }
        else {
            Set-Content $yamlPath $content -NoNewline
            Write-Host "  FIXED: $($folder.Name) (priority: $priorityValue)" -ForegroundColor Green
        }
        $fixed++
    }
    catch {
        Write-Host "  ERROR: $($folder.Name): $_" -ForegroundColor Red
        $errors++
    }
}

Write-Host ""
Write-Host "Results:" -ForegroundColor White
Write-Host "  Fixed:   $fixed" -ForegroundColor Green
Write-Host "  Skipped: $skipped" -ForegroundColor Gray
Write-Host "  Errors:  $errors" -ForegroundColor $(if ($errors -gt 0) { 'Red' } else { 'Gray' })

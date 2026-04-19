# One-time migration: move artifacts/recommendations.yaml into plan.yaml
# Run: pwsh migrate-recommendations.ps1

$plansDir = $env:TENDRIL_PLANS
if (-not $plansDir) {
    $tendrilHome = $env:TENDRIL_HOME
    if (-not $tendrilHome) { Write-Error "TENDRIL_HOME not set"; exit 1 }
    $plansDir = Join-Path $tendrilHome "Plans"
}

if (-not (Test-Path $plansDir)) { Write-Error "Plans dir not found: $plansDir"; exit 1 }

$migrated = 0
$skipped = 0
$failed = 0
$deleted = 0

foreach ($dir in Get-ChildItem -Path $plansDir -Directory | Where-Object { $_.Name -match '^\d{5}-' }) {
    $recsPath = Join-Path $dir.FullName "artifacts" "recommendations.yaml"
    $planPath = Join-Path $dir.FullName "plan.yaml"

    if (-not (Test-Path $recsPath)) { continue }

    if (-not (Test-Path $planPath)) {
        Write-Warning "No plan.yaml in $($dir.Name), skipping"
        $skipped++
        continue
    }

    try {
        $recsContent = Get-Content -Raw $recsPath
        if ([string]::IsNullOrWhiteSpace($recsContent)) {
            Remove-Item $recsPath -Force
            $deleted++
            continue
        }

        $planContent = Get-Content -Raw $planPath

        # Check if plan.yaml already has recommendations with content
        if ($planContent -match '(?m)^recommendations:\s*\n\s*- title:') {
            # Already migrated, just delete the legacy file
            Remove-Item $recsPath -Force
            $deleted++
            continue
        }

        # Parse the recommendations from the legacy file to validate
        # Simple check: does it look like a YAML list?
        if ($recsContent -notmatch '(?m)^- title:') {
            Write-Warning "Unexpected format in $($dir.Name)/artifacts/recommendations.yaml, skipping"
            $skipped++
            continue
        }

        # Indent the recommendations content for embedding under the recommendations: key
        $recsLines = $recsContent -split "`n"
        $indented = ($recsLines | ForEach-Object {
            if ($_ -match '^\s*$') { $_ }
            else { "  $_" }
        }) -join "`n"

        # Remove trailing empty recommendations key if present (e.g. "recommendations:" or "recommendations: []")
        $newPlan = $planContent -replace '(?m)^recommendations:\s*(\[\])?\s*$', ''
        $newPlan = $newPlan.TrimEnd() + "`nrecommendations:`n$indented`n"

        Set-Content -Path $planPath -Value $newPlan -NoNewline
        Remove-Item $recsPath -Force

        # Clean up empty artifacts directory
        $artifactsDir = Join-Path $dir.FullName "artifacts"
        if ((Test-Path $artifactsDir) -and ((Get-ChildItem $artifactsDir).Count -eq 0)) {
            Remove-Item $artifactsDir -Force
        }

        $migrated++
        Write-Host "  Migrated $($dir.Name)" -ForegroundColor Green
    }
    catch {
        Write-Warning "Failed $($dir.Name): $_"
        $failed++
    }
}

Write-Host ""
Write-Host "Done: $migrated migrated, $deleted empty/already-done deleted, $skipped skipped, $failed failed"

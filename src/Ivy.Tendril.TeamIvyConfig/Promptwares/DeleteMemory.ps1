Get-ChildItem -Path $PSScriptRoot -Directory | ForEach-Object {
    $memoryPath = Join-Path $_.FullName "Memory"
    if (Test-Path $memoryPath) {
        Get-ChildItem -Path $memoryPath -File -Force | Where-Object Name -ne '.gitkeep' | Remove-Item -Force
    }
}

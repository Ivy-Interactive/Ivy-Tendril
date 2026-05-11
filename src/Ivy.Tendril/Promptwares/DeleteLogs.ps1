Get-ChildItem -Path $PSScriptRoot -Directory | ForEach-Object {
    $logsPath = Join-Path $_.FullName "Logs"
    if (Test-Path $logsPath) {
        Get-ChildItem -Path $logsPath -File | Remove-Item -Force
    }
}

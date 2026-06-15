$env:TENDRIL_NOT_MASTER = "1"
dotnet watch --project "$PSScriptRoot" --browse --find-available-port

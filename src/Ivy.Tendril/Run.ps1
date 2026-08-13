$env:TENDRIL_NOT_MASTER = "1"
dotnet watch --project "$PSScriptRoot" --find-available-port

param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$Args
)

$ErrorActionPreference = "Stop"

# Use dotnet run to invoke Tendril CLI
$tendrilProject = "D:\Repos\_Ivy\Ivy-Tendril\src\Ivy.Tendril\Ivy.Tendril.csproj"

& dotnet run --project $tendrilProject --no-build -- @Args
exit $LASTEXITCODE

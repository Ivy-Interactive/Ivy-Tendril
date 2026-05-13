$ErrorActionPreference = "Stop"

$templateId = "e17c9769-2d66-4ee3-aea5-499eb0b3255d"
$templateFile = Join-Path $PSScriptRoot "template.html"

if (-not (Test-Path $templateFile)) {
    Write-Error "template.html not found at $templateFile"
    exit 1
}

if (-not $env:RESEND_API_KEY) {
    Write-Error "RESEND_API_KEY environment variable is not set"
    exit 1
}

$html = Get-Content $templateFile -Raw

$body = @{
    name    = "Tendril Newsletter"
    subject = "{{{SUBJECT}}}"
    from    = "niels@ivyinteractive.se"
    html    = $html
} | ConvertTo-Json -Depth 10

$response = Invoke-RestMethod `
    -Uri "https://api.resend.com/templates/$templateId" `
    -Method Patch `
    -Headers @{ Authorization = "Bearer $env:RESEND_API_KEY" } `
    -ContentType "application/json" `
    -Body $body

Write-Host "Template updated: $($response.id)" -ForegroundColor Green

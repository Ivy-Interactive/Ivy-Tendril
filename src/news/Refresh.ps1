# Refresh News CDN
# Uploads news.json and images to Azure Blob Storage (public/tendril/news/*)
# Available at https://cdn.ivy.app/tendril/news/*

$ErrorActionPreference = "Stop"

$secretsDir = [System.IO.Path]::Combine(
    [Environment]::GetFolderPath("ApplicationData"),
    "Microsoft", "UserSecrets", "ac99f729-7d40-4ab3-bde1-d85b0a51423b"
)

$secretsFile = Join-Path $secretsDir "secrets.json"
if (-not (Test-Path $secretsFile)) {
    Write-Host "No user-secrets found. Run SetupLocalDevelopment.ps1 first." -ForegroundColor Red
    exit 1
}

$secrets = Get-Content $secretsFile -Raw | ConvertFrom-Json
$connectionString = $secrets.'Cdn:ConnectionString'

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    Write-Host "Cdn:ConnectionString not found in user-secrets." -ForegroundColor Red
    exit 1
}

$scriptDir = $PSScriptRoot
$container = "public"
$destPrefix = "tendril/news"

# Upload news.json
Write-Host "Uploading news.json..." -ForegroundColor Yellow
az storage blob upload `
    --connection-string $connectionString `
    --container-name $container `
    --name "$destPrefix/news.json" `
    --file (Join-Path $scriptDir "news.json") `
    --overwrite `
    --content-type "application/json" `
    --no-progress `
    --output none

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to upload news.json" -ForegroundColor Red
    exit 1
}
Write-Host "  news.json uploaded" -ForegroundColor Green

# Upload images
$imagesDir = Join-Path $scriptDir "images"
if (Test-Path $imagesDir) {
    $imageFiles = Get-ChildItem $imagesDir -File -Recurse
    foreach ($file in $imageFiles) {
        $relativePath = $file.FullName.Substring($imagesDir.Length + 1) -replace '\\', '/'
        $blobName = "$destPrefix/images/$relativePath"

        $contentType = switch ($file.Extension.ToLower()) {
            ".png"  { "image/png" }
            ".jpg"  { "image/jpeg" }
            ".jpeg" { "image/jpeg" }
            ".gif"  { "image/gif" }
            ".svg"  { "image/svg+xml" }
            ".webp" { "image/webp" }
            default { "application/octet-stream" }
        }

        Write-Host "Uploading $relativePath..." -ForegroundColor Yellow
        az storage blob upload `
            --connection-string $connectionString `
            --container-name $container `
            --name $blobName `
            --file $file.FullName `
            --overwrite `
            --content-type $contentType `
            --no-progress `
            --output none

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Failed to upload $relativePath" -ForegroundColor Red
            exit 1
        }
        Write-Host "  $relativePath uploaded" -ForegroundColor Green
    }
}

Write-Host "`nRefresh complete. Content available at https://cdn.ivy.app/tendril/news/" -ForegroundColor Cyan

param(
    [string]$Version = "1.0.0",
    [string]$ConnectionString = "",
    [string]$Container = "fortiva-releases"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $root "deploy-ionos"

if (-not $ConnectionString) {
    $ConnectionString = $env:AZURE_STORAGE_CONNECTION_STRING
}
if (-not $ConnectionString) {
    Write-Host "AZURE_STORAGE_CONNECTION_STRING not set — skipping Azure mirror."
    exit 0
}

if (-not (Test-Path $staging)) {
    throw "Staging folder missing: $staging"
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI (az) not found on PATH"
}

Write-Host "Uploading release $Version to Azure Blob container '$Container'..."
az storage container create --name $Container --connection-string $ConnectionString --public-access off -o none 2>$null

Get-ChildItem $staging -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($staging.Length).TrimStart('\').Replace('\', '/')
    Write-Host "  -> $relative"
    az storage blob upload `
        --connection-string $ConnectionString `
        --container-name $Container `
        --name $relative `
        --file $_.FullName `
        --overwrite true `
        -o none
}

Write-Host "Azure mirror complete (backup only — clients use studio.icmclab.cloud)."

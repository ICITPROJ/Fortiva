# Ensures extension/manifest.json contains a stable RSA public key.
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\extension\manifest.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.PSObject.Properties.Name -contains 'key' -and -not [string]::IsNullOrWhiteSpace([string]$manifest.key)) {
    Write-Host "Extension key already present in manifest.json"
    exit 0
}

$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$keyB64 = [Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo())
$manifest | Add-Member -NotePropertyName key -NotePropertyValue $keyB64 -Force
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8
Write-Host "Added stable extension key to manifest.json"

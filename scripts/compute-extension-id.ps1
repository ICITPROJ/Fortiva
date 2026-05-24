# Computes Chrome extension ID from manifest.json "key" field.
# Keep in sync with Fortiva.Core.BrowserBridge.ExtensionIdHelper

param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\extension\manifest.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Manifest not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if (-not $manifest.key) {
    throw "manifest.json is missing required 'key' field for stable extension ID."
}

$keyBytes = [Convert]::FromBase64String([string]$manifest.key)
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $hash = $sha.ComputeHash($keyBytes)
}
finally {
    $sha.Dispose()
}

$idChars = New-Object char[] 32
for ($i = 0; $i -lt 16; $i++) {
    $idChars[$i * 2] = [char]([int][char]'a' + ($hash[$i] -shr 4))
    $idChars[$i * 2 + 1] = [char]([int][char]'a' + ($hash[$i] -band 0xF))
}

return -join $idChars

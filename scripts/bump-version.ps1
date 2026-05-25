# Bump patch version in Directory.Build.props (and extension manifest).
param(
    [string]$Version = "",
    [switch]$Patch,
    [switch]$Set
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$propsPath = Join-Path $root 'Directory.Build.props'
$manifestPath = Join-Path $root 'extension\manifest.json'

[xml]$props = Get-Content -LiteralPath $propsPath
$pg = $props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
$current = [string]$pg.Version

if ($Set) {
    if (-not $Version) { throw "Use -Set -Version x.y.z" }
    $newVersion = $Version
} elseif ($Version) {
    $newVersion = $Version
} elseif ($Patch) {
    $parts = $current.Split('.')
    if ($parts.Length -lt 3) { throw "Invalid current version: $current" }
    $parts[2] = [string]([int]$parts[2] + 1)
    $newVersion = $parts -join '.'
} else {
    throw "Specify -Patch, -Set -Version x.y.z, or -Version x.y.z"
}

if ($newVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid version: $newVersion" }

$assembly = "$newVersion.0"
$pg.Version = $newVersion
$pg.AssemblyVersion = $assembly
$pg.FileVersion = $assembly
$props.Save($propsPath)

if (Test-Path $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.version = $newVersion
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

Write-Host "Version bumped: $current -> $newVersion" -ForegroundColor Green
Write-Host "  Directory.Build.props"
Write-Host "  extension/manifest.json"

return $newVersion

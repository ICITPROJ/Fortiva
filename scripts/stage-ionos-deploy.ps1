param(
    [string]$Version = "1.0.0",
    [string]$OutDir = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) {
    $OutDir = Join-Path $root "deploy-ionos"
}

$manifest = Join-Path $root "packaging\releases\latest.personal.json"
if (-not (Test-Path $manifest)) {
    throw "Manifest missing: $manifest (run publish-release-manifest.ps1 first)"
}

$personalInstaller = Join-Path $root "dist\installers\FortivaPersonal-$Version-Setup.exe"
if (-not (Test-Path $personalInstaller)) {
    throw "Personal installer missing: $personalInstaller"
}

$releaseRoot = Join-Path $OutDir "fortiva\releases"
$versionDir = Join-Path $releaseRoot $Version

Remove-Item -Recurse -Force $OutDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $versionDir | Out-Null

Copy-Item $manifest (Join-Path $releaseRoot "latest.personal.json") -Force
Copy-Item $personalInstaller (Join-Path $versionDir (Split-Path $personalInstaller -Leaf)) -Force

foreach ($edition in @('Enterprise', 'Admin')) {
    $src = Join-Path $root "dist\installers\Fortiva$edition-$Version-Setup.exe"
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $versionDir (Split-Path $src -Leaf)) -Force
    }
}

Write-Host "Staged IONOS deploy payload at $OutDir"
Get-ChildItem $OutDir -Recurse -File | ForEach-Object {
    Write-Host "  $($_.FullName.Substring($OutDir.Length).TrimStart('\')) ($([math]::Round($_.Length/1MB, 2)) MB)"
}

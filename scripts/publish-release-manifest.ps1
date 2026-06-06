param(

    [string]$Version = "1.0.0",

    [int]$MaxWindowsBuildTested = 26100,

    [string]$InstallerPath = "",

    [string]$OutPath = "",

    [string]$Repository = ""

)



$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent



if (-not $InstallerPath) {

    $InstallerPath = Join-Path $root "dist\installers\FortivaPersonal-$Version-Setup.exe"

}

if (-not (Test-Path $InstallerPath)) {

    throw "Installer not found: $InstallerPath"

}



if (-not $Repository) {

    $Repository = $env:GITHUB_REPOSITORY

}

if (-not $Repository) {

    $Repository = "ICITPROJ/Fortiva"

}



$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $InstallerPath).Hash.ToLowerInvariant()

$fileName = [IO.Path]::GetFileName($InstallerPath)

$tag = "v$Version"

$installerUrl = "https://github.com/$Repository/releases/download/$tag/$fileName"



if (-not $OutPath) {

    $OutPath = Join-Path $root "packaging\releases\latest.personal.json"

}



$manifest = [ordered]@{

    schemaVersion = 1

    edition = "Personal"

    version = $Version

    releasedAt = (Get-Date).ToUniversalTime().ToString("o")

    minWindowsBuild = 19041

    maxWindowsBuildTested = $MaxWindowsBuildTested

    installerUrl = $installerUrl

    installerSha256 = $hash

    installerArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /FORCECLOSEAPPLICATIONS"

    releaseNotes = "Fortiva Personal $Version"

}



New-Item -ItemType Directory -Force (Split-Path $OutPath) | Out-Null

$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $OutPath



Write-Host "Wrote $OutPath"

Write-Host "  version:      $Version"

Write-Host "  repository:   $Repository"

Write-Host "  installerUrl: $installerUrl"

Write-Host "  sha256:       $hash"

Write-Host ""

Write-Host "GitHub Release assets for tag $tag :"

Write-Host "  latest.personal.json"

Write-Host "  $fileName"

Write-Host ""

Write-Host "Client check URL:"

Write-Host "  https://github.com/$Repository/releases/latest/download/latest.personal.json"



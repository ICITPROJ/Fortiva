#Requires -Version 5.1
<#
.SYNOPSIS
    Build and deploy Fortiva Personal to the local install folder.

.DESCRIPTION
    Publishes Release win-x64, stops running Fortiva processes, mirrors to
    %LOCALAPPDATA%\Programs\icmclab studio\Fortiva Personal, syncs extension staging,
    and relaunches the app. Restart + unlock required for bridge to activate.
#>
param(
    [switch]$SkipLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "FortivaPersonalPaths.ps1")

$root = Split-Path $PSScriptRoot -Parent
$distPersonal = Join-Path $root "dist\Fortiva.Personal"
$distBridge = Join-Path $root "dist\BrowserBridge"
$install = Join-Path $env:LOCALAPPDATA "Programs\icmclab studio\Fortiva Personal"
$staging = Join-Path $env:LOCALAPPDATA "FortivaPersonal\extension"

Write-Host "Publishing Fortiva Personal..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\Fortiva.Personal\Fortiva.Personal.csproj") `
    -c Release -r win-x64 --self-contained -p:PublishSingleFile=false `
    -o $distPersonal -v q
if ($LASTEXITCODE -ne 0) { throw "Fortiva.Personal publish failed." }

Write-Host "Publishing bridge host..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\Fortiva.BrowserBridge.Host\Fortiva.BrowserBridge.Host.csproj") `
    -c Release -r win-x64 --self-contained -p:PublishSingleFile=false `
    -o $distBridge -v q
if ($LASTEXITCODE -ne 0) { throw "Bridge host publish failed." }

$bridgeDest = Join-Path $distPersonal "BrowserBridge"
if (Test-Path $bridgeDest) { Remove-Item $bridgeDest -Recurse -Force }
New-Item -ItemType Directory -Force $bridgeDest | Out-Null
Copy-Item (Join-Path $distBridge "*") $bridgeDest -Recurse -Force

$extSrc = Join-Path $root "extension"
$extDest = Join-Path $distPersonal "extension"
New-Item -ItemType Directory -Force $extDest | Out-Null
Get-ChildItem $extSrc -File | Where-Object {
    $_.Name -ne "content.js" -and $_.Name -notlike "com.fortiva.browserbridge*.json"
} | Copy-Item -Destination $extDest -Force

Write-Host "Stopping Fortiva processes..." -ForegroundColor Yellow
Get-Process -Name "Fortiva.Personal","Fortiva.BrowserBridge.Host" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Assert-FortivaInstallTargetSafe -InstallPath $install
Write-Host "Deploying to $install (user vault/settings in %APPDATA%\Fortiva are not modified)..." -ForegroundColor Cyan
robocopy $distPersonal $install /MIR /XD logs /R:2 /W:2 /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit $LASTEXITCODE" }

Write-Host "Syncing extension staging..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $staging | Out-Null
robocopy $extSrc $staging /MIR /XF content.js com.fortiva.browserbridge*.json /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null

Write-Host "Repairing native messaging manifest..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "Repair-BrowserExtension.ps1") | Out-Host

$version = (Get-Item (Join-Path $install "Fortiva.Personal.exe")).VersionInfo.FileVersion
Write-Host "Deployed Fortiva Personal $version" -ForegroundColor Green

if (-not $SkipLaunch) {
    Start-Process (Join-Path $install "Fortiva.Personal.exe")
    Write-Host "Launched Fortiva. Unlock vault, then Settings -> Browser extension -> Connect browser." -ForegroundColor Yellow
}

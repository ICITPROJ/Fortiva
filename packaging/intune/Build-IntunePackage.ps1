#Requires -Version 5.1
<#
.SYNOPSIS
    Build FortivaEnterprise.intunewin for Microsoft Endpoint Manager.
.DESCRIPTION
    Stages the Enterprise setup EXE and Intune provisioning scripts, then wraps them with
    Microsoft's IntuneWinAppUtil.exe (PATH, packaging/tools, or auto-download).
.PARAMETER Version
    Product version. Read from Directory.Build.props when omitted.
.PARAMETER InstallerPath
    Explicit path to FortivaEnterprise-*-Setup.exe.
.PARAMETER OutputDir
    Folder for FortivaEnterprise.intunewin (default: dist\intune).
.PARAMETER SkipDownload
    Do not download IntuneWinAppUtil when missing locally.
#>
param(
    [string]$Version = "",
    [string]$InstallerPath = "",
    [string]$OutputDir = "",
    [switch]$SkipDownload
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$manifestPath = Join-Path $PSScriptRoot "intune-package.json"

function Read-ProductVersion {
    if (-not [string]::IsNullOrWhiteSpace($Version)) { return $Version.Trim() }
    $props = Join-Path $root "Directory.Build.props"
    $m = Select-String -Path $props -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $m) { throw "Could not read Version from Directory.Build.props" }
    return $m.Matches[0].Groups[1].Value
}

function Resolve-EnterpriseInstaller {
    if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
        if (-not (Test-Path -LiteralPath $InstallerPath)) {
            throw "Installer not found: $InstallerPath"
        }
        return (Resolve-Path -LiteralPath $InstallerPath).Path
    }

    $dir = Join-Path $root "dist\installers"
    $candidate = Get-ChildItem -Path $dir -Filter "FortivaEnterprise-*-Setup.exe" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $candidate) {
        throw "No FortivaEnterprise-*-Setup.exe under $dir. Run build-release.ps1 and build-installers.ps1 first."
    }
    return $candidate.FullName
}

function Resolve-IntuneWinAppUtil {
    $cmd = Get-Command "IntuneWinAppUtil.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $localTool = Join-Path $root "packaging\tools\IntuneWinAppUtil.exe"
    if (Test-Path -LiteralPath $localTool) { return $localTool }

    if ($SkipDownload) {
        throw @"
IntuneWinAppUtil.exe not found. Add it to PATH, place it at packaging\tools\IntuneWinAppUtil.exe,
or re-run without -SkipDownload to fetch Microsoft's Win32 Content Prep Tool.
"@
    }

    Write-Host "Downloading Microsoft Win32 Content Prep Tool..." -ForegroundColor Cyan
    $toolsDir = Join-Path $root "packaging\tools"
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    $zipPath = Join-Path $toolsDir "Microsoft-Win32-Content-Prep-Tool.zip"

    $zipUrl = "https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool/archive/refs/heads/master.zip"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
    Expand-Archive -LiteralPath $zipPath -DestinationPath $toolsDir -Force

    $exe = Get-ChildItem -Path $toolsDir -Filter "IntuneWinAppUtil.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $exe) {
        throw "Downloaded archive did not contain IntuneWinAppUtil.exe"
    }

    Copy-Item -LiteralPath $exe.FullName -Destination $localTool -Force
    return $localTool
}

$version = Read-ProductVersion
$installer = Resolve-EnterpriseInstaller
$setupName = Split-Path $installer -Leaf

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root "dist\intune"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$staging = Join-Path $OutputDir "package-staging"
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $staging | Out-Null

Copy-Item -LiteralPath $installer -Destination (Join-Path $staging $setupName) -Force
foreach ($script in @(
        "Install-FortivaEnterprise.ps1",
        "Deploy-Intune.ps1",
        "Detect-FortivaEnterprise.ps1",
        "intune-package.json"
    )) {
    $src = Join-Path $PSScriptRoot $script
    if (-not (Test-Path -LiteralPath $src)) {
        throw "Missing packaging script: $src"
    }
    Copy-Item -LiteralPath $src -Destination (Join-Path $staging $script) -Force
}

$util = Resolve-IntuneWinAppUtil
Write-Host "Packaging Fortiva Enterprise $version with $util" -ForegroundColor Cyan
Write-Host "  Source: $staging" -ForegroundColor DarkGray
Write-Host "  Setup:  $setupName" -ForegroundColor DarkGray

$proc = Start-Process -FilePath $util `
    -ArgumentList @("-c", $staging, "-s", $setupName, "-o", $OutputDir, "-q") `
    -Wait -PassThru -NoNewWindow
if ($proc.ExitCode -ne 0) {
    throw "IntuneWinAppUtil exited with code $($proc.ExitCode)"
}

$package = Join-Path $OutputDir "FortivaEnterprise.intunewin"
if (-not (Test-Path -LiteralPath $package)) {
    $fallback = Get-ChildItem -Path $OutputDir -Filter "*.intunewin" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($fallback) {
        if ($fallback.FullName -ne $package) {
            Move-Item -LiteralPath $fallback.FullName -Destination $package -Force
        }
    }
}

if (-not (Test-Path -LiteralPath $package)) {
    throw "Expected output package not found under $OutputDir"
}

Write-Host "Created $package" -ForegroundColor Green
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Write-Host "Intune install command: $($manifest.installCommand)" -ForegroundColor DarkGray
    Write-Host "Intune detection script: $($manifest.detectionScript)" -ForegroundColor DarkGray
    Write-Host "Proactive remediation: $($manifest.remediationScript)" -ForegroundColor DarkGray
}

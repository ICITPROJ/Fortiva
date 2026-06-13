#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Silent Fortiva Enterprise install for Intune Win32 deployment.
.DESCRIPTION
    Runs the Enterprise setup EXE with /VERYSILENT, then registers HKLM native messaging
    and verifies ExtensionInstallForcelist policy keys.
.PARAMETER InstallerPath
    Full path to FortivaEnterprise-*-Setup.exe. When omitted, uses the newest match under dist\installers.
.PARAMETER SkipNativeMessagingRepair
    Skip post-install HKLM native messaging registration (installer normally writes these).
#>
param(
    [string]$InstallerPath = "",
    [switch]$SkipNativeMessagingRepair
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function Resolve-Installer {
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
        throw "No FortivaEnterprise-*-Setup.exe under $dir — run build-release.ps1 and build-installers.ps1 first."
    }
    return $candidate.FullName
}

$installer = Resolve-Installer
Write-Host "Installing Fortiva Enterprise: $installer" -ForegroundColor Cyan

$proc = Start-Process -FilePath $installer -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru -Wait
if ($proc.ExitCode -ne 0) {
    throw "Installer exited with code $($proc.ExitCode)"
}

if (-not $SkipNativeMessagingRepair) {
    & (Join-Path $PSScriptRoot "Deploy-Intune.ps1") -RepairOnly
}

Write-Host "Fortiva Enterprise installed successfully." -ForegroundColor Green

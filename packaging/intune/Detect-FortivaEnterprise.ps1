#Requires -Version 5.1
<#
.SYNOPSIS
    Intune Win32 custom detection script for Fortiva Enterprise.
.DESCRIPTION
    Verifies the Enterprise client binary and machine-wide HKLM native messaging bindings
    for browsers that are actually installed on the endpoint.
    Exit 0 = installed and compliant; exit 1 = missing or drifted.
#>
param(
    [string]$HostName = "com.fortiva.browserbridge.enterprise",
    [string]$MainExecutable = "Fortiva.Enterprise.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

function Write-DetectionLog {
    param([string]$Message)
    Write-Output $Message
}

function Fail-Detection {
    param([string]$Message)
    Write-DetectionLog $Message
    exit 1
}

function Test-EnterpriseExecutable {
    param([string]$FileName)

    foreach ($base in @(${env:ProgramFiles}, ${env:ProgramFiles(x86)})) {
        if ([string]::IsNullOrWhiteSpace($base)) { continue }
        $candidate = Join-Path $base "icmclab studio\Fortiva Enterprise\$FileName"
        if (Test-Path -LiteralPath $candidate) {
            return $true
        }
    }

    return $false
}

function Test-ChromeInstalled {
    foreach ($path in @(
            (Join-Path ${env:ProgramFiles} "Google\Chrome\Application\chrome.exe"),
            (Join-Path ${env:ProgramFiles(x86)} "Google\Chrome\Application\chrome.exe")
        )) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path)) {
            return $true
        }
    }

    return $false
}

function Test-EdgeInstalled {
    foreach ($path in @(
            (Join-Path ${env:ProgramFiles} "Microsoft\Edge\Application\msedge.exe"),
            (Join-Path ${env:ProgramFiles(x86)} "Microsoft\Edge\Application\msedge.exe")
        )) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path)) {
            return $true
        }
    }

    return $false
}

function Test-NativeMessagingHive {
    param([string]$HiveSubKey)

    $path = "HKLM:\$HiveSubKey\$HostName"
    if (-not (Test-Path -LiteralPath $path)) {
        return $false
    }

    $value = (Get-ItemProperty -LiteralPath $path -Name "(default)" -ErrorAction SilentlyContinue).'(default)'
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $false
    }

    if (-not (Test-Path -LiteralPath $value)) {
        return $false
    }

    return $true
}

if (-not (Test-EnterpriseExecutable -FileName $MainExecutable)) {
    Fail-Detection "Fortiva Enterprise core binary missing under Program Files."
}

$chromeInstalled = Test-ChromeInstalled
$edgeInstalled = Test-EdgeInstalled

if ($chromeInstalled -and -not (Test-NativeMessagingHive -HiveSubKey "SOFTWARE\Google\Chrome\NativeMessagingHosts")) {
    Fail-Detection "Chrome is installed, but Fortiva native messaging HKLM configuration is missing."
}

if ($edgeInstalled -and -not (Test-NativeMessagingHive -HiveSubKey "SOFTWARE\Microsoft\Edge\NativeMessagingHosts")) {
    Fail-Detection "Edge is installed, but Fortiva native messaging HKLM configuration is missing."
}

Write-DetectionLog "Fortiva Enterprise detection gate passed cleanly."
exit 0

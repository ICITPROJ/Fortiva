#Requires -Version 5.1
<#
.SYNOPSIS
    Intune Win32 custom detection script for Fortiva Enterprise.
.DESCRIPTION
    Verifies the Enterprise client binary and machine-wide HKLM native messaging bindings.
    Exit 0 = installed and compliant; exit 1 = missing or drifted.
#>
param(
    [string]$HostName = "com.fortiva.browserbridge.enterprise",
    [string]$MainExecutable = "Fortiva.Enterprise.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

$exeOk = Test-EnterpriseExecutable -FileName $MainExecutable
$chromeOk = Test-NativeMessagingHive -HiveSubKey "SOFTWARE\Google\Chrome\NativeMessagingHosts"
$edgeOk = Test-NativeMessagingHive -HiveSubKey "SOFTWARE\Microsoft\Edge\NativeMessagingHosts"

if ($exeOk -and $chromeOk -and $edgeOk) {
    exit 0
}

exit 1

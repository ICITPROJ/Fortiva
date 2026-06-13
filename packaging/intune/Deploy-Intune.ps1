#Requires -Version 5.1
<#
.SYNOPSIS
    Enterprise browser bridge provisioning for Intune / GPO-managed endpoints.
.DESCRIPTION
    Writes the native messaging manifest under Program Files and registers machine-wide
    Chrome/Edge NativeMessagingHosts keys (HKLM). Optionally verifies ExtensionInstallForcelist.
.PARAMETER InstallRoot
    Fortiva Enterprise install folder. Auto-detected when omitted.
.PARAMETER ExtensionId
    Chromium extension ID (default: stable Fortiva Autofill ID from manifest key).
.PARAMETER ExtensionUpdateUrl
    CRX update manifest URL for ExtensionInstallForcelist.
.PARAMETER RepairOnly
    Skip force-install verification; only repair native messaging HKLM + manifest file.
#>
param(
    [string]$InstallRoot = "",
    [string]$ExtensionId = "llkpcnbhmhpenahlcdnbbfmkdfkgnpnj",
    [string]$ExtensionUpdateUrl = "https://github.com/ICITPROJ/Fortiva/releases/latest/download/fortiva-extension-updates.xml",
    [switch]$RepairOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$HostName = "com.fortiva.browserbridge.enterprise"

function Resolve-InstallRoot {
    if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
        if (-not (Test-Path -LiteralPath $InstallRoot)) {
            throw "Install root not found: $InstallRoot"
        }
        return (Resolve-Path -LiteralPath $InstallRoot).Path
    }

    foreach ($base in @(
            ${env:ProgramFiles},
            ${env:ProgramFiles(x86)}
        )) {
        if ([string]::IsNullOrWhiteSpace($base)) { continue }
        $candidate = Join-Path $base "icmclab studio\Fortiva Enterprise"
        if (Test-Path (Join-Path $candidate "Fortiva.Enterprise.exe")) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Fortiva Enterprise not found under Program Files. Install first or pass -InstallRoot."
}

function Write-NativeMessagingManifest {
    param(
        [string]$Root,
        [string]$ExtId
    )

    $bridgeExe = Join-Path $Root "BrowserBridge\Fortiva.BrowserBridge.Host.exe"
    if (-not (Test-Path -LiteralPath $bridgeExe)) {
        throw "Bridge host missing: $bridgeExe"
    }

    $manifestDir = Join-Path $Root "NativeMessaging"
    New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null
    $manifestPath = Join-Path $manifestDir "$HostName.json"
    $bridgeFull = (Resolve-Path -LiteralPath $bridgeExe).Path

    $payload = @{
        name             = $HostName
        description      = "Fortiva local credential bridge"
        path             = $bridgeFull
        type             = "stdio"
        allowed_origins  = @("chrome-extension://$ExtId/")
    }
    $payload | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    return (Resolve-Path -LiteralPath $manifestPath).Path
}

function Set-MachineNativeHostKey {
    param(
        [string]$HiveSubKey,
        [string]$ManifestPath
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        throw "Manifest missing: $ManifestPath"
    }

    $full = (Resolve-Path -LiteralPath $ManifestPath).Path
    $path = "HKLM:\$HiveSubKey\$HostName"
    New-Item -Path $path -Force | Out-Null
    Set-ItemProperty -Path $path -Name "(default)" -Value $full -Type String
    Write-Host "  HKLM\$HiveSubKey\$HostName -> $full" -ForegroundColor DarkGray
}

function Test-ForceInstallList {
    param([string]$ExpectedValue)

    $keys = @(
        "HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist",
        "HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist"
    )

    foreach ($keyPath in $keys) {
        if (-not (Test-Path $keyPath)) {
            Write-Warning "Missing policy key: $keyPath"
            return $false
        }
        $props = Get-ItemProperty -Path $keyPath
        $found = $false
        foreach ($name in $props.PSObject.Properties.Name) {
            if ($name -in @("PSPath", "PSParentPath", "PSChildName", "PSDrive", "PSProvider")) { continue }
            if ([string]$props.$name -eq $ExpectedValue) { $found = $true; break }
        }
        if (-not $found) {
            Write-Warning "Force-install value not found in $keyPath (expected: $ExpectedValue)"
            return $false
        }
    }

    return $true
}

$installRoot = Resolve-InstallRoot
Write-Host "Fortiva Enterprise install root: $installRoot" -ForegroundColor Cyan

Write-Host "Writing native messaging manifest..." -ForegroundColor Cyan
$manifestPath = Write-NativeMessagingManifest -Root $installRoot -ExtId $ExtensionId

Write-Host "Registering HKLM native messaging hosts..." -ForegroundColor Cyan
Set-MachineNativeHostKey -HiveSubKey "SOFTWARE\Google\Chrome\NativeMessagingHosts" -ManifestPath $manifestPath
Set-MachineNativeHostKey -HiveSubKey "SOFTWARE\Microsoft\Edge\NativeMessagingHosts" -ManifestPath $manifestPath

$sidecar = Join-Path $installRoot "BrowserBridge\bridge-host.sha256"
$bridgeExe = Join-Path $installRoot "BrowserBridge\Fortiva.BrowserBridge.Host.exe"
if (Test-Path -LiteralPath $bridgeExe) {
    if (Test-Path -LiteralPath $sidecar) {
        $expected = (Get-Content -LiteralPath $sidecar -Raw).Trim().ToLowerInvariant()
        $actual = (Get-FileHash -LiteralPath $bridgeExe -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($expected -ne $actual) {
            Write-Warning "bridge-host.sha256 mismatch — redeploy or run Repair-BrowserExtension.ps1 on clients."
        }
    }
}

if (-not $RepairOnly) {
    $forceValue = "$ExtensionId;$ExtensionUpdateUrl"
    Write-Host "Verifying ExtensionInstallForcelist..." -ForegroundColor Cyan
    if (Test-ForceInstallList -ExpectedValue $forceValue) {
        Write-Host "ExtensionInstallForcelist OK" -ForegroundColor Green
    } else {
        Write-Warning @"
ExtensionInstallForcelist not fully configured. The Enterprise installer sets:
  HKLM\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist
  HKLM\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist
Value: $forceValue
"@
    }
}

Write-Host "Enterprise native messaging provisioning complete." -ForegroundColor Green

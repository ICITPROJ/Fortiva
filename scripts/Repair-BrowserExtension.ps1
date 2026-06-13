#Requires -Version 5.1
<#
.SYNOPSIS
    Repairs native-messaging manifest and extension staging for Fortiva Personal.
.DESCRIPTION
    Fixes stale bridge paths in com.fortiva.browserbridge.personal.json (common after
    test runs or partial installs). Re-registers Chrome/Edge native messaging hosts.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "FortivaPersonalPaths.ps1")

$install = Join-Path $env:LOCALAPPDATA "Programs\icmclab studio\Fortiva Personal"
$bridgeExe = Join-Path $install "BrowserBridge\Fortiva.BrowserBridge.Host.exe"
$extSrc = Join-Path (Split-Path $PSScriptRoot -Parent) "extension"
$staging = Join-Path $env:LOCALAPPDATA "FortivaPersonal\extension"
$manifestDir = Join-Path $env:LOCALAPPDATA "FortivaPersonal\NativeMessaging"
$hostName = "com.fortiva.browserbridge.personal"
$manifestFile = Join-Path $manifestDir "$hostName.json"

if (-not (Test-Path $bridgeExe)) {
    throw "Bridge host missing: $bridgeExe. Run Deploy-FortivaPersonal.ps1 first."
}

$extManifest = Join-Path $extSrc "manifest.json"
if (-not (Test-Path $extManifest)) { throw "Extension manifest missing: $extManifest" }
$extDoc = Get-Content $extManifest -Raw | ConvertFrom-Json
$extId = "llkpcnbhmhpenahlcdnbbfmkdfkgnpnj"
if ($extDoc.PSObject.Properties.Name -contains "key") {
    # packaged id from manifest key is stable for this extension
}

Write-Host "Staging extension..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $staging | Out-Null
robocopy $extSrc $staging /MIR /XF content.js com.fortiva.browserbridge*.json /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null

$payload = @{
    name = $hostName
    description = "Fortiva local credential bridge"
    path = (Resolve-Path $bridgeExe).Path
    type = "stdio"
    allowed_origins = @("chrome-extension://$extId/")
}
New-Item -ItemType Directory -Force $manifestDir | Out-Null
$payload | ConvertTo-Json -Depth 4 | Set-Content $manifestFile -Encoding UTF8

foreach ($subKey in @(
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName",
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
)) {
    New-Item -Path $subKey -Force | Out-Null
    Set-ItemProperty -Path $subKey -Name "(default)" -Value $manifestFile
}

$sidecar = Join-Path (Split-Path $bridgeExe) "bridge-host.sha256"
$hash = (Get-FileHash $bridgeExe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path $sidecar -Value $hash -Encoding ascii -NoNewline

Write-Host "Repaired native host manifest:" -ForegroundColor Green
Write-Host "  $manifestFile"
Write-Host "  bridge: $bridgeExe"
Write-Host "  hash:   $sidecar"
Write-Host ""
Write-Host "Next: edge://extensions -> Reload Fortiva Autofill, then try Fill on a login page." -ForegroundColor Yellow
exit 0

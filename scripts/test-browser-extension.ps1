# Verifies Fortiva browser extension staging + native messaging registration.
# Run after "Connect browser" in Fortiva Settings, or after build-release for dev.
param(
    [switch]$Personal,
    [switch]$Enterprise
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$edition = if ($Enterprise) { 'Enterprise' } else { 'Personal' }
$appDataRoot = Join-Path $env:LOCALAPPDATA "Fortiva$edition"
$staging = Join-Path $appDataRoot 'extension'
$hostName = if ($Enterprise) { 'com.fortiva.browserbridge.enterprise' } else { 'com.fortiva.browserbridge.personal' }
$manifestPath = Join-Path $appDataRoot "NativeMessaging\$hostName.json"

Write-Host ""
Write-Host "Fortiva browser extension check ($edition)" -ForegroundColor Cyan
Write-Host ("=" * 50)

$ok = $true
function Check($label, [scriptblock]$test) {
    try {
        if (& $test) {
            Write-Host "  OK   $label" -ForegroundColor Green
        } else {
            Write-Host "  FAIL $label" -ForegroundColor Red
            $script:ok = $false
        }
    } catch {
        Write-Host "  FAIL $label - $($_.Exception.Message)" -ForegroundColor Red
        $script:ok = $false
    }
}

Check "Extension folder exists" { Test-Path $staging }
Check "manifest.json present" { Test-Path (Join-Path $staging 'manifest.json') }
Check "background.js present" { Test-Path (Join-Path $staging 'background.js') }
Check "popup.js present" { Test-Path (Join-Path $staging 'popup.js') }
Check "content.js NOT shipped" { -not (Test-Path (Join-Path $staging 'content.js')) }

Check "Native messaging manifest" { Test-Path $manifestPath }
Check "Chrome registry key" {
    $k = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"
    (Get-ItemProperty -Path $k -ErrorAction SilentlyContinue).'(default)' -eq $manifestPath
}
Check "Edge registry key" {
    $k = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName"
    (Get-ItemProperty -Path $k -ErrorAction SilentlyContinue).'(default)' -eq $manifestPath
}

if (Test-Path $manifestPath) {
    $nm = Get-Content $manifestPath -Raw | ConvertFrom-Json
    Check "Bridge exe exists" { Test-Path $nm.path }
    $extId = & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\compute-extension-id.ps1') -ManifestPath (Join-Path $staging 'manifest.json')
    Check "allowed_origins matches extension ID" {
        $origin = "chrome-extension://$extId/"
        $nm.allowed_origins -contains $origin
    }
    Write-Host ""
    Write-Host "Extension ID: $extId" -ForegroundColor DarkGray
    Write-Host "Bridge host:  $($nm.path)" -ForegroundColor DarkGray
}

Write-Host ""
if (-not (Test-Path $staging)) {
    Write-Host "Nothing staged yet. In Fortiva: Settings -> Browser extension -> Connect browser" -ForegroundColor Yellow
    exit 1
}

Write-Host "Manual test (5 minutes):" -ForegroundColor Cyan
Write-Host "  1. Open Fortiva and unlock your vault"
Write-Host "  2. Settings -> Browser extension -> Connect browser (if not done)"
Write-Host "  3. In Edge/Chrome toolbar, confirm the Fortiva icon appears"
Write-Host "  4. Save a login in Fortiva for a site (e.g. github.com)"
Write-Host "  5. Open that site's sign-in page in the browser"
Write-Host "  6. Click Fortiva icon -> Fill credentials on this page"
Write-Host ""
Write-Host "Staging folder: $staging" -ForegroundColor DarkGray
Write-Host ""

if ($ok) {
    Write-Host "Automated checks passed." -ForegroundColor Green
    exit 0
}

Write-Host "Some checks failed — run Connect browser in Fortiva Settings." -ForegroundColor Red
exit 1

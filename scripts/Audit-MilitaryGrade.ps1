#Requires -Version 5.1
<#
.SYNOPSIS
    Military-grade compliance gate — static checks + automated test suites.

.DESCRIPTION
    Verifies SR-* requirements from docs/MILITARY-GRADE-SPEC.md before release.
    Does not replace manual IONOS fill or Enterprise registry inspection.
#>
param(
    [switch]$SkipDeploy,
    [switch]$SkipMatrix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$failures = 0

function Write-Step([string]$Title) {
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

function Assert-FileContains([string]$Path, [string]$Pattern, [string]$Label) {
    if (-not (Test-Path $Path)) {
        Write-Host "FAIL $Label - missing file $Path" -ForegroundColor Red
        $script:failures++
        return
    }
    if (Select-String -Path $Path -Pattern $Pattern -Quiet) {
        Write-Host "PASS $Label" -ForegroundColor Green
    } else {
        Write-Host "FAIL $Label - pattern not found in $Path" -ForegroundColor Red
        $script:failures++
    }
}

Write-Step "Static compliance (SR-BRIDGE / SR-EXT)"
$core = Join-Path $repo "src\Fortiva.Core"
Assert-FileContains (Join-Path $core "BrowserBridge\BridgeTokenBroker.cs") "validateClients:\s*true" "SR-BRIDGE-02 token pipe validation"
Assert-FileContains (Join-Path $core "BrowserBridge\BrowserBridgeServer.cs") "validateClients:\s*true" "SR-BRIDGE-02 credential pipe validation"
Assert-FileContains (Join-Path $core "BrowserBridge\BridgeUnlockBroker.cs") "validateClients:\s*true" "SR-BRIDGE-03 unlock pipe validation"
Assert-FileContains (Join-Path $core "BrowserBridge\BridgeCredentialProtector.cs") "BridgeCredentialProtector" "SR-BRIDGE-05 pipe password sealing"
Assert-FileContains (Join-Path $core "BrowserBridge\DomainSafety.cs") "ContainsAceEncodedLabel" "SR-BRIDGE-08 punycode rejection"
Assert-FileContains (Join-Path $core "BrowserBridge\BrowserExtensionConstants.cs") "StableExtensionId" "SR-EXT-02 pinned extension ID"
Assert-FileContains (Join-Path $repo "extension\background.js") "sender\.id !== chrome\.runtime\.id" "SR-EXT-03 extension sender check"
Assert-FileContains (Join-Path $repo "docs\MILITARY-GRADE-SPEC.md") "SR-BRIDGE" "Military spec document present"

Write-Step "Version alignment"
$props = Get-Content (Join-Path $repo "Directory.Build.props") -Raw
$manifest = Get-Content (Join-Path $repo "extension\manifest.json") -Raw | ConvertFrom-Json
if ($props -match '<Version>([\d.]+)</Version>') {
    $appVer = $Matches[1]
    if ($manifest.version -eq $appVer) {
        Write-Host "PASS ext=$($manifest.version) app=$appVer" -ForegroundColor Green
    } else {
        Write-Host "FAIL version mismatch ext=$($manifest.version) app=$appVer" -ForegroundColor Red
        $failures++
    }
} else {
    Write-Host "FAIL could not read Directory.Build.props version" -ForegroundColor Red
    $failures++
}

Write-Step "Unit tests (bridge security suite)"
$filter = @(
    "FullyQualifiedName~BridgeNativeForwarder",
    "FullyQualifiedName~BridgeAppLauncher",
    "FullyQualifiedName~BridgeFillNonce",
    "FullyQualifiedName~BridgePingClassifier",
    "FullyQualifiedName~BridgeCredentialProtector",
    "FullyQualifiedName~DomainSafety",
    "FullyQualifiedName~VaultEntryWebsite",
    "FullyQualifiedName~BrowserBridgeInstall"
) -join "|"

dotnet test (Join-Path $repo "tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj") -c Release --filter $filter -v q
if ($LASTEXITCODE -ne 0) { $failures++ }

Write-Step "AppHost tests"
dotnet test (Join-Path $repo "tests\Fortiva.AppHost.Tests\Fortiva.AppHost.Tests.csproj") -c Release -v q
if ($LASTEXITCODE -ne 0) { $failures++ }

if (-not $SkipDeploy) {
    Write-Step "Deploy + repair manifest"
    & (Join-Path $PSScriptRoot "Deploy-FortivaPersonal.ps1") -SkipLaunch
    if ($LASTEXITCODE -ne 0) { $failures++ }
}

if (-not $SkipMatrix) {
    Write-Step "Extension matrix"
    Get-Process -Name "Fortiva.Personal", "Fortiva.BrowserBridge.Host" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    & (Join-Path $PSScriptRoot "Test-ExtensionFullMatrix.ps1")
    if ($LASTEXITCODE -ne 0) { $failures++ }
}

Write-Host ""
if ($failures -eq 0) {
    Write-Host "MILITARY GATE PASSED - see docs/MILITARY-GRADE-SPEC.md tier B+ checklist." -ForegroundColor Green
    Write-Host "Manual: unlock vault, run Test-ExtensionRequireReady.ps1, test IONOS fill." -ForegroundColor Yellow
    exit 0
}

Write-Host "MILITARY GATE FAILED - $failures check(s)" -ForegroundColor Red
exit 1

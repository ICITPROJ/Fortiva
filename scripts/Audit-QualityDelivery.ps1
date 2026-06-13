#Requires -Version 5.1
<#
.SYNOPSIS
    Serious pre-delivery quality audit for Fortiva Personal browser extension.
#>
param([int]$StressIterations = 25)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$failures = 0

function Step([string]$Title) {
    Write-Host ""
    Write-Host "======== $Title ========" -ForegroundColor Cyan
}

Step "Repair extension + native messaging"
& (Join-Path $repo "scripts\Repair-BrowserExtension.ps1") | Out-Host
if ($LASTEXITCODE -ne 0) { $failures++; Write-Host "Repair failed" -ForegroundColor Red }

Step "Build Release"
dotnet build (Join-Path $repo "src\Fortiva.Personal\Fortiva.Personal.csproj") -c Release -p:Platform=x64 -v q | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Step "Deploy to local install"
& (Join-Path $repo "scripts\Deploy-FortivaPersonal.ps1") -SkipLaunch | Out-Host
if ($LASTEXITCODE -ne 0) { $failures++ }

Step "Core bridge unit tests (stable set)"
$coreFilter = @(
    "FullyQualifiedName~BridgeNativeForwarder",
    "FullyQualifiedName~BridgeAppLauncher",
    "FullyQualifiedName~BridgePingClassifier",
    "FullyQualifiedName~BridgeFillNonce",
    "FullyQualifiedName~VaultEntryWebsite",
    "FullyQualifiedName~BridgePresence",
    "FullyQualifiedName~DomainSafety"
) -join "|"
dotnet test (Join-Path $repo "tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj") -c Release --filter $coreFilter -v q | Out-Host
if ($LASTEXITCODE -ne 0) { $failures++ }

Step "IONOS domain matching tests (fast unit + e2e)"
Get-Process Fortiva.Personal, Fortiva.BrowserBridge.Host -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
$ionosFilter = "FullyQualifiedName~VaultEntryWebsiteTests|FullyQualifiedName~DomainSafetyTests"
dotnet test (Join-Path $repo "tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj") -c Release --filter $ionosFilter -v q | Out-Host
if ($LASTEXITCODE -ne 0) { $failures++ }

Step "AppHost tests"
dotnet test (Join-Path $repo "tests\Fortiva.AppHost.Tests\Fortiva.AppHost.Tests.csproj") -c Release -p:Platform=x64 -v q | Out-Host
if ($LASTEXITCODE -ne 0) { $failures++ }

Step "Extension full matrix"
& (Join-Path $repo "scripts\Test-ExtensionFullMatrix.ps1") -StressIterations $StressIterations | Out-Host
if ($LASTEXITCODE -ne 0) { $failures++ }

Step "Legacy audit smoke"
& (Join-Path $repo "scripts\Audit-BrowserExtension.ps1") -LiveIterations 5 | Out-Host
if ($LASTEXITCODE -ne 0) { $failures++ }

Write-Host ""
if ($failures -gt 0) {
    Write-Host "QUALITY AUDIT FAILED ($failures step(s))" -ForegroundColor Red
    exit 1
}
Write-Host "QUALITY AUDIT PASSED" -ForegroundColor Green
Write-Host "Manual gate: unlock Fortiva, then .\scripts\Test-ExtensionRequireReady.ps1" -ForegroundColor Yellow
exit 0

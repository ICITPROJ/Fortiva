#Requires -Version 5.1

<#

.SYNOPSIS

    Full browser-extension audit: live bridge smoke test, unit tests, manifest check.



.DESCRIPTION

    Run before asking users to QA. Live bridge test runs before the long unit-test suite

    so named-pipe state from tests cannot interfere with native-host verification.

#>

param(

    [int]$LiveIterations = 10

)



Set-StrictMode -Version Latest

$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent



function Write-Step([string]$Title) {

    Write-Host ""

    Write-Host "=== $Title ===" -ForegroundColor Cyan

}



$failures = 0



Write-Step "Build and deploy bridge host"

dotnet build "$repo\src\Fortiva.BrowserBridge.Host\Fortiva.BrowserBridge.Host.csproj" -c Release | Out-Host

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "scripts\Deploy-FortivaPersonal.ps1") -SkipLaunch | Out-Host

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }



Write-Step "Extension manifest sanity"

$manifestPath = Join-Path $repo "extension\manifest.json"

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if ($manifest.manifest_version -ne 3) { throw "Extension must use manifest v3." }

if (-not (Test-Path (Join-Path $repo "extension\background.js"))) { throw "background.js missing." }

if (-not (Test-Path (Join-Path $repo "extension\popup.js"))) { throw "popup.js missing." }

Write-Host "Extension version: $($manifest.version)" -ForegroundColor Green



Write-Step "Live bridge test (Fortiva running locked -> ping=locked)"

Get-Process -Name "Fortiva.BrowserBridge.Host" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$appExe = Join-Path $env:LOCALAPPDATA "Programs\icmclab studio\Fortiva Personal\Fortiva.Personal.exe"
if (-not (Get-Process -Name "Fortiva.Personal" -ErrorAction SilentlyContinue) -and (Test-Path $appExe)) {
    Start-Process $appExe -WorkingDirectory (Split-Path $appExe) | Out-Null
    Start-Sleep -Seconds 8
}

# Wait until native ping responds (unlock broker ready)
$hostExe = Join-Path $env:LOCALAPPDATA "Programs\icmclab studio\Fortiva Personal\BrowserBridge\Fortiva.BrowserBridge.Host.exe"
$pingReady = $false
for ($i = 0; $i -lt 30; $i++) {
    Get-Process -Name "Fortiva.BrowserBridge.Host" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $hostExe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $b = [System.Text.Encoding]::UTF8.GetBytes('{"command":"ping"}')
    $p.StandardInput.BaseStream.Write([BitConverter]::GetBytes([int]$b.Length), 0, 4)
    $p.StandardInput.BaseStream.Write($b, 0, $b.Length)
    $p.StandardInput.Close()
    $lb = New-Object byte[] 4
    if ($p.StandardOutput.BaseStream.BeginRead($lb, 0, 4, $null, $null).AsyncWaitHandle.WaitOne(8000)) {
        $len = [BitConverter]::ToInt32($lb, 0)
        if ($len -gt 0) { $pingReady = $true; break }
    }
    Start-Sleep -Seconds 1
}
if (-not $pingReady) {
    Write-Host "WARN: Ping not ready before live test; Fortiva may still be starting." -ForegroundColor Yellow
}



& "$repo\scripts\Test-BrowserBridge.ps1" -Iterations $LiveIterations 2>&1 | Out-Host

if ($LASTEXITCODE -ne 0) { $failures++ }



Write-Step "Core unit tests (bridge-focused; skip heavy vault integration)"

Get-Process -Name "Fortiva.Personal","Fortiva.BrowserBridge.Host","testhost" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# Fast, deterministic tests only — full BrowserBridge suite includes pipe stress/e2e that hang in automation.
$coreFilter = @(
    "FullyQualifiedName~BridgeNativeForwarderTests",
    "FullyQualifiedName~BridgeAppLauncherTests",
    "FullyQualifiedName~BridgePingClassifierTests",
    "FullyQualifiedName~BridgeFillNonceTests",
    "FullyQualifiedName~VaultEntryWebsite"
) -join "|"

dotnet test "$repo\tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj" -c Release `
    --filter $coreFilter 2>&1 | Out-Host

if ($LASTEXITCODE -ne 0) { $failures++ }



Write-Step "Cold launch smoke (execute_fill should start Fortiva when stopped)"

Get-Process -Name "Fortiva.Personal" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$hostExe = Join-Path $env:LOCALAPPDATA "Programs\icmclab studio\Fortiva Personal\BrowserBridge\Fortiva.BrowserBridge.Host.exe"
if (Test-Path $hostExe) {
    $launchJob = Start-Job -ScriptBlock {
        param($Exe)
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $Exe
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $p = [System.Diagnostics.Process]::Start($psi)
        $json = '{"command":"execute_fill","payload":{"domain":"login.ionos.co.uk","url":"https://login.ionos.co.uk/"}}'
        $b = [System.Text.Encoding]::UTF8.GetBytes($json)
        $p.StandardInput.BaseStream.Write([BitConverter]::GetBytes([int]$b.Length), 0, 4)
        $p.StandardInput.BaseStream.Write($b, 0, $b.Length)
        $p.StandardInput.Close()
        $null = $p.WaitForExit(120000)
    } -ArgumentList $hostExe

    $launched = $false
    for ($i = 0; $i -lt 35; $i++) {
        if (Get-Process -Name "Fortiva.Personal" -ErrorAction SilentlyContinue) {
            $launched = $true
            Write-Host "Fortiva launched within $($i + 1)s" -ForegroundColor Green
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $launched) {
        Write-Host "WARN: Fortiva did not launch within 35s on cold execute_fill." -ForegroundColor Yellow
        $failures++
    }

    Stop-Job $launchJob -ErrorAction SilentlyContinue | Out-Null
    Remove-Job $launchJob -Force -ErrorAction SilentlyContinue
}



Write-Step "AppHost tests"

dotnet test "$repo\tests\Fortiva.AppHost.Tests\Fortiva.AppHost.Tests.csproj" -c Release -p:Platform=x64 2>&1 | Out-Host

if ($LASTEXITCODE -ne 0) { $failures++ }



Write-Host ""

if ($failures -gt 0) {

    Write-Host "AUDIT FAILED ($failures step(s))." -ForegroundColor Red

    exit 1

}



Write-Host "AUDIT PASSED - extension bridge path verified in automation." -ForegroundColor Green

Write-Host "Note: run Test-BrowserBridge.ps1 -RequireReady after unlocking to validate the full fill path." -ForegroundColor Yellow

exit 0


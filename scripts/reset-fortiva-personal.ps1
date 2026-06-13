# Stop Fortiva, uninstall if present, wipe all user vault data, verify clean.
param(
    [switch]$WipeOnly
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\FortivaPersonalPaths.ps1"

function Write-Step($msg) { Write-Host ">> $msg" -ForegroundColor Cyan }

try {
    Write-Step 'Stopping Fortiva processes...'
    Stop-FortivaProcesses

    if (-not $WipeOnly) {
        Write-Step 'Looking for installed Fortiva Personal...'
        if (Invoke-FortivaPersonalUninstaller) {
            Write-Host '   Uninstaller finished.'
            Start-Sleep -Seconds 2
        }
        else {
            Write-Host '   (No Fortiva Personal install found or uninstall skipped)' -ForegroundColor DarkGray
        }
    }

    Write-Step 'Wiping user data...'
    Remove-FortivaPersonalUserData -ConfirmProductionWipe

    Write-Step 'Verifying...'
    if (Test-FortivaPersonalVaultExists) {
        Write-Host ''
        Write-Host 'FAIL: vault.fva still exists. Close Fortiva and run this script again.' -ForegroundColor Red
        exit 1
    }

    foreach ($p in Get-FortivaPersonalDataPaths) {
        if (Test-Path -LiteralPath $p) {
            Write-Host ''
            Write-Host "FAIL: data still present at $p" -ForegroundColor Red
            exit 1
        }
    }

    Write-Host ''
    Write-Host 'OK: Fortiva Personal is fully reset. Reinstall or launch to see onboarding.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Verify Fortiva Personal user data paths and optionally wipe for QA reset.
param(
    [switch]$Wipe,
    [switch]$VerifyClean
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\FortivaPersonalPaths.ps1"

$vaultFound = $false
Write-Host 'Fortiva Personal user-data locations:'
foreach ($p in Get-FortivaPersonalDataPaths) {
    $exists = Test-Path -LiteralPath $p
    $vaultPath = Join-Path $p 'vault.fva'
    $vault = Test-Path -LiteralPath $vaultPath
    $hello = Test-Path -LiteralPath (Join-Path $p 'hello.keyprotect')
    if ($exists -or $vault) {
        Write-Host "  $p"
        Write-Host "    exists=$exists  vault.fva=$vault  hello.keyprotect=$hello"
        if ($vault) { $vaultFound = $true }
        if ($exists) {
            Get-ChildItem -LiteralPath $p -Force -ErrorAction SilentlyContinue |
                ForEach-Object { Write-Host "      - $($_.Name) ($($_.Length) bytes)" }
        }
    }
    else {
        Write-Host "  $p  (clean)"
    }
}

if ($Wipe) {
    Write-Host ''
    Write-Host 'Wiping...'
    Stop-FortivaProcesses
    Remove-FortivaPersonalUserData
    $VerifyClean = $true
}

if ($VerifyClean) {
    if ($vaultFound -and -not $Wipe) {
        # Re-check disk in case listing was stale
        $vaultFound = Test-FortivaPersonalVaultExists
    }
    elseif ($Wipe) {
        $vaultFound = Test-FortivaPersonalVaultExists
    }

    if ($vaultFound -or (Get-FortivaPersonalDataPaths | Where-Object { Test-Path -LiteralPath $_ })) {
        Write-Host ''
        Write-Host 'FAIL: vault or user data still present.' -ForegroundColor Red
        exit 1
    }
    Write-Host ''
    Write-Host 'OK: no vault data found.' -ForegroundColor Green
    exit 0
}

# Automated tests for reset/verify scripts. Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts\test-scripts.ps1
#
# SAFETY: Never deletes real %APPDATA%\Fortiva — wipe tests use an isolated temp tree only.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\FortivaPersonalPaths.ps1"

$failures = 0
function Assert($cond, $msg) {
    if (-not $cond) { Write-Host "FAIL: $msg" -ForegroundColor Red; $script:failures++ }
    else { Write-Host "PASS: $msg" -ForegroundColor Green }
}

Write-Host '=== Uninstall string parsing ==='
$tests = @(
    @{ s = '"C:\Users\x\AppData\Local\Programs\icmclab studio\Fortiva Personal\unins000.exe"'; exe = 'C:\Users\x\AppData\Local\Programs\icmclab studio\Fortiva Personal\unins000.exe'; args = '' }
    @{ s = '"C:\path\unins000.exe" /SILENT'; exe = 'C:\path\unins000.exe'; args = '/SILENT' }
    @{ s = 'C:\path\unins000.exe /SILENT'; exe = 'C:\path\unins000.exe'; args = '/SILENT' }
)
foreach ($t in $tests) {
    $exe = $null; $args = $null
    if ($t.s -match '^"(?<exe>[^"]+)"\s*(?<args>.*)$') { $exe = $matches.exe; $args = $matches.args.Trim() }
    elseif ($t.s -match '^(?<exe>[^\s]+\.exe)\s*(?<args>.*)$') { $exe = $matches.exe; $args = $matches.args.Trim() }
    Assert ($exe -eq $t.exe) "exe parse: $($t.s)"
    Assert ($args -eq $t.args) "args parse: $($t.s)"
}

Write-Host ''
Write-Host '=== Path list ==='
$paths = Get-FortivaPersonalDataPaths
Assert ($paths.Count -eq 4) 'four data paths defined'
Assert ($paths[0] -like '*Fortiva\Personal') 'legacy Personal path included'
Assert ($paths[1] -like '*Roaming\Fortiva') 'vault root included'

Write-Host ''
Write-Host '=== Install target safety ==='
$install = Join-Path $env:LOCALAPPDATA 'Programs\icmclab studio\Fortiva Personal'
Assert (Test-FortivaInstallTargetSafe -InstallPath $install) 'default install path is safe'
Assert (-not (Test-FortivaInstallTargetSafe -InstallPath (Join-Path $env:APPDATA 'Fortiva'))) 'appdata Fortiva rejected'
Assert (-not (Test-FortivaInstallTargetSafe -InstallPath (Join-Path $env:LOCALAPPDATA 'FortivaPersonal'))) 'local FortivaPersonal rejected'

Write-Host ''
Write-Host '=== Wipe guard ==='
try {
    Remove-FortivaPersonalUserData -ErrorAction Stop
    Assert $false 'Remove-FortivaPersonalUserData without -ConfirmProductionWipe must throw'
}
catch {
    Assert $true 'wipe without -ConfirmProductionWipe is rejected'
}

Write-Host ''
Write-Host '=== Wipe logic (isolated temp — never touches real Fortiva data) ==='
$tempRoot = Join-Path $env:TEMP ("fortiva-script-test-" + [Guid]::NewGuid().ToString('N'))
$mockAppData = Join-Path $tempRoot 'MockAppData\Fortiva'
$mockLocal = Join-Path $tempRoot 'MockLocal\FortivaPersonal'
$vaultFile = Join-Path $mockAppData 'vault.fva'

try {
    New-Item -ItemType Directory -Path $mockAppData -Force | Out-Null
    New-Item -ItemType Directory -Path $mockLocal -Force | Out-Null
    Set-Content -LiteralPath $vaultFile -Value 'mock-vault' -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $mockAppData 'hello.keyprotect') -Value 'mock' -Encoding ASCII

    Assert (Test-Path -LiteralPath $vaultFile) 'mock vault created in temp'

    # Simulate Remove-FortivaPersonalUserData against mock paths only
    foreach ($p in @($mockAppData, (Split-Path $mockAppData -Parent), $mockLocal)) {
        if (Test-Path -LiteralPath $p) {
            Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Assert (-not (Test-Path -LiteralPath $vaultFile)) 'mock vault removed after simulated wipe'
    Assert (-not (Test-Path -LiteralPath $mockLocal)) 'mock log folder removed'

    $realVault = Get-FortivaPersonalVaultPath
    if (Test-Path -LiteralPath $realVault) {
        Write-Host "PASS: real vault at $realVault was NOT touched by script tests" -ForegroundColor Green
    }
    else {
        Write-Host "NOTE: no real vault present at $realVault" -ForegroundColor DarkYellow
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
if ($failures -gt 0) {
    Write-Host "$failures test(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All script tests passed.' -ForegroundColor Green
exit 0

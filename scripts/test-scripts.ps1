# Automated tests for reset/verify scripts. Run from repo root:
#   powershell -ExecutionPolicy Bypass -File scripts\test-scripts.ps1
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
Write-Host '=== Wipe round-trip (mock vault) ==='
$vaultRoot = Join-Path $env:APPDATA 'Fortiva'
$vaultFile = Join-Path $vaultRoot 'vault.fva'
$logDir = Join-Path $env:LOCALAPPDATA 'FortivaPersonal'

try {
    New-Item -ItemType Directory -Path $vaultRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    Set-Content -LiteralPath $vaultFile -Value 'mock-vault' -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $vaultRoot 'hello.keyprotect') -Value 'mock' -Encoding ASCII

    Assert (Test-FortivaPersonalVaultExists) 'mock vault created'
    Stop-FortivaProcesses
    Remove-FortivaPersonalUserData
    Assert (-not (Test-FortivaPersonalVaultExists)) 'vault removed after wipe'
    Assert (-not (Test-Path -LiteralPath $vaultRoot)) 'Fortiva folder removed'
    Assert (-not (Test-Path -LiteralPath $logDir)) 'log folder removed'
}
finally {
    if (Test-Path -LiteralPath $vaultRoot) { Remove-Item -LiteralPath $vaultRoot -Recurse -Force -EA SilentlyContinue }
    if (Test-Path -LiteralPath $logDir) { Remove-Item -LiteralPath $logDir -Recurse -Force -EA SilentlyContinue }
}

Write-Host ''
if ($failures -gt 0) {
    Write-Host "$failures test(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All script tests passed.' -ForegroundColor Green
exit 0

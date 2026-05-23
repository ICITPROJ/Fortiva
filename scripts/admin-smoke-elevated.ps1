# Admin smoke boot — must run elevated. Writes result to dist\admin-smoke-result.txt
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$log = Join-Path $root 'dist\admin-smoke-result.txt'
$admExe = Join-Path $root 'dist\Fortiva.Admin\Fortiva.Admin.exe'
$crashLog = Join-Path $env:LOCALAPPDATA 'FortivaAdmin\fortiva-crash.log'

. (Join-Path $PSScriptRoot 'FortivaPersonalPaths.ps1')

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    "FAIL: not elevated" | Set-Content $log
    exit 1
}
if (-not (Test-Path $admExe)) {
    "FAIL: missing $admExe" | Set-Content $log
    exit 1
}

try {
    for ($i = 1; $i -le 5; $i++) {
        Stop-FortivaProcesses
        $beforeLog = if (Test-Path $crashLog) { (Get-Item $crashLog).Length } else { 0 }
        $proc = Start-FortivaSmokeProcess -FilePath $admExe -ExtraEnvironment @{
            FORTIVA_ALLOW_DEV_LICENSE_KEY = '1'
        }
        Start-Sleep -Seconds 12
        if ($proc.HasExited) { throw "Admin boot $i crashed (exit $($proc.ExitCode))" }
        $proc.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) }
        if (Test-Path $crashLog) {
            $afterLog = (Get-Item $crashLog).Length
            if ($afterLog -gt $beforeLog) { throw "Admin boot $i wrote crash log" }
        }
    }
    "PASS: 5 x Admin smoke boot (elevated) at $(Get-Date -Format o)" | Set-Content $log
    exit 0
}
catch {
    "FAIL: $($_.Exception.Message)" | Set-Content $log
    exit 1
}

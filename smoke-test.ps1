param(
    [string]$ExePath = '',
    [int]$HoldSeconds = 15
)
$root = Split-Path $PSScriptRoot -Parent
if (-not $ExePath) { $ExePath = Join-Path $root 'dist\Fortiva.Personal\Fortiva.Personal.exe' }

$prevSkip = $env:FORTIVA_SKIP_AUTO_UPDATE
$env:FORTIVA_SKIP_AUTO_UPDATE = '1'
try {
    Write-Host "Launching Fortiva.Personal..."
    $proc = Start-Process -FilePath $ExePath -PassThru
    $half = [Math]::Max(5, [int]($HoldSeconds / 2))
    Start-Sleep -Seconds $half
    if ($proc.HasExited) {
        Write-Host "CRASHED  ExitCode=$($proc.ExitCode)"
        $logPath = "$env:LOCALAPPDATA\FortivaPersonal\fortiva-crash.log"
        if (-not (Test-Path $logPath)) { $logPath = "$env:LOCALAPPDATA\FortivaPersonal\startup-crash.log" }
        if (Test-Path $logPath) {
            Write-Host "=== Crash log ==="
            Get-Content $logPath | Select-Object -Last 30
        }
        exit 1
    }

    Write-Host "RUNNING  PID=$($proc.Id)"
    Start-Sleep -Seconds ($HoldSeconds - $half)
    Write-Host "App ran for $HoldSeconds seconds without crash. Closing..."
    $proc.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 2
    if (-not $proc.HasExited) { $proc.Kill() }
    Write-Host "SMOKE TEST PASSED"
    exit 0
}
finally {
    if ($null -eq $prevSkip) { Remove-Item env:FORTIVA_SKIP_AUTO_UPDATE -ErrorAction SilentlyContinue }
    else { $env:FORTIVA_SKIP_AUTO_UPDATE = $prevSkip }
}

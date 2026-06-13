#Requires -Version 5.1
<#
.SYNOPSIS
    Waits for vault unlock then validates full bridge + IONOS credential match path.

.DESCRIPTION
    1. Opens Fortiva if needed
    2. Polls ping until ready (up to 5 minutes - unlock manually)
    3. Runs Test-BrowserBridge.ps1 -RequireReady
#>
param(
    [string]$ListDomain = "login.ionos.co.uk",
    [string]$ListUrl = "https://login.ionos.co.uk/",
    [int]$WaitMinutes = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$install = Join-Path $env:LOCALAPPDATA "Programs\icmclab studio\Fortiva Personal"
$appExe = Join-Path $install "Fortiva.Personal.exe"
$hostExe = Join-Path $install "BrowserBridge\Fortiva.BrowserBridge.Host.exe"

if (-not (Test-Path $hostExe)) { throw "Bridge host not found. Deploy Fortiva Personal first." }

function Clear-StaleBridgeHosts {
    $hosts = @(Get-Process Fortiva.BrowserBridge.Host -ErrorAction SilentlyContinue)
    if ($hosts.Count -gt 0) {
        $hosts | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }
}

function Invoke-Ping {
    Clear-StaleBridgeHosts
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $hostExe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $p = [Diagnostics.Process]::Start($psi)
    try {
        $json = '{"command":"ping"}'
        $b = [Text.Encoding]::UTF8.GetBytes($json)
        $p.StandardInput.BaseStream.Write([BitConverter]::GetBytes([int]$b.Length), 0, 4)
        $p.StandardInput.BaseStream.Write($b, 0, $b.Length)
        $p.StandardInput.Close()
        $lb = New-Object byte[] 4
        if (-not $p.StandardOutput.BaseStream.Read($lb, 0, 4)) { return $null }
        $len = [BitConverter]::ToInt32($lb, 0)
        if ($len -le 0 -or $len -gt 1048576) { return $null }
        $buf = New-Object byte[] $len
        $o = 0
        while ($o -lt $len) {
            $read = $p.StandardOutput.BaseStream.Read($buf, $o, $len - $o)
            if ($read -le 0) { return $null }
            $o += $read
        }
        [Text.Encoding]::UTF8.GetString($buf) | ConvertFrom-Json
    }
    catch {
        $null
    }
    finally {
        if (-not $p.HasExited) {
            try { $p.Kill() } catch { }
        }
        $p.Dispose()
    }
}

Write-Host "=== Require-Ready Test ===" -ForegroundColor Cyan
Write-Host "Unlock Fortiva in the app window (master password recommended)."
Write-Host "Domain: $ListDomain"
Write-Host ""

if (-not (Get-Process Fortiva.Personal -ErrorAction SilentlyContinue) -and (Test-Path $appExe)) {
    Write-Host "Starting Fortiva..."
    Start-Process $appExe -WorkingDirectory (Split-Path $appExe)
    Start-Sleep -Seconds 8
}

$deadline = (Get-Date).AddMinutes($WaitMinutes)
$lastStatus = ""
$noResponseCount = 0
while ((Get-Date) -lt $deadline) {
    $ping = Invoke-Ping
    $status = if ($ping) { [string]$ping.status } else { "no_response" }
    if ($status -eq "no_response") {
        $noResponseCount++
        if ($noResponseCount -eq 3) {
            Write-Host "No bridge response - restarting Fortiva..." -ForegroundColor Yellow
            Clear-StaleBridgeHosts
            Get-Process Fortiva.Personal -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            if (Test-Path $appExe) {
                Start-Process $appExe -WorkingDirectory (Split-Path $appExe)
                Start-Sleep -Seconds 8
            }
            $noResponseCount = 0
        }
    }
    else {
        $noResponseCount = 0
    }

    if ($status -ne $lastStatus) {
        $msg = if ($ping -and $ping.message) { [string]$ping.message } else { "" }
        if ($msg) {
            Write-Host ("[{0:HH:mm:ss}] ping={1} - {2}" -f (Get-Date), $status, $msg)
        }
        else {
            Write-Host ("[{0:HH:mm:ss}] ping={1}" -f (Get-Date), $status)
        }
        $lastStatus = $status
    }
    if ($status -eq "ready") {
        Write-Host "Vault ready - running full bridge test..." -ForegroundColor Green
        & (Join-Path $PSScriptRoot "Test-BrowserBridge.ps1") -RequireReady -Iterations 10 -ListDomain $ListDomain -ListUrl $ListUrl
        exit $LASTEXITCODE
    }
    Start-Sleep -Seconds 2
}

Write-Host "TIMEOUT: Vault not ready within $WaitMinutes minutes." -ForegroundColor Red
Write-Host "Unlock Fortiva, then re-run: C:\Repo\Github\Fortiva\scripts\Test-ExtensionRequireReady.ps1"
exit 1

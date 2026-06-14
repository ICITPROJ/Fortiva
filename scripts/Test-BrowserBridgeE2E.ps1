#Requires -Version 5.1
<#
.SYNOPSIS
    E2E acceptance test for Fortiva browser bridge (get_status_and_matches).

.DESCRIPTION
    Validates Option A+B rebuild:
    - One-shot native host (spawn per request, exit after response)
    - get_status_and_matches returns status + matches in < 2s when vault unlocked
    - No orphan bridge host processes after test

.EXAMPLE
    .\scripts\Test-BrowserBridgeE2E.ps1 -RequireReady
#>
param(
    [switch]$RequireReady,
    [string]$ListDomain = "login.ionos.co.uk",
    [string]$ListUrl = "https://login.ionos.co.uk/",
    [int]$MaxResponseMs = 2000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-InstallRoot {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\icmclab studio\Fortiva Personal"),
        (Join-Path $PSScriptRoot "..\dist\Fortiva.Personal")
    )
    foreach ($path in $candidates) {
        $full = [System.IO.Path]::GetFullPath($path)
        if (Test-Path (Join-Path $full "Fortiva.Personal.exe")) { return $full }
    }
    throw "Fortiva Personal install not found."
}

function Test-FortivaRunning {
    return [bool](Get-Process -Name "Fortiva.Personal" -ErrorAction SilentlyContinue)
}

function Get-BridgeHostCount {
    return @(Get-Process -Name "Fortiva.BrowserBridge.Host" -ErrorAction SilentlyContinue).Count
}

function Invoke-NativeHostOnce {
    param(
        [string]$HostExe,
        [string]$JsonPayload,
        [int]$ResponseTimeoutMs = 6000
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $HostExe
    $psi.WorkingDirectory = [System.IO.Path]::GetDirectoryName($HostExe)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.CreateNoWindow = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    if (-not $proc) {
        return @{ Ok = $false; Error = "spawn_failed"; ElapsedMs = 0; Raw = $null }
    }

    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($JsonPayload)
        $header = [BitConverter]::GetBytes([int]$bytes.Length)
        $proc.StandardInput.BaseStream.Write($header, 0, 4)
        $proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $proc.StandardInput.BaseStream.Flush()
        $proc.StandardInput.Close()

        $stream = $proc.StandardOutput.BaseStream
        $lenBuf = New-Object byte[] 4
        $headerWait = $stream.BeginRead($lenBuf, 0, 4, $null, $null)
        if (-not $headerWait.AsyncWaitHandle.WaitOne($ResponseTimeoutMs)) {
            return @{ Ok = $false; Error = "timeout"; ElapsedMs = $sw.ElapsedMilliseconds; Raw = $null }
        }
        $read = $stream.EndRead($headerWait)
        if ($read -lt 4) {
            return @{ Ok = $false; Error = "truncated_header"; ElapsedMs = $sw.ElapsedMilliseconds; Raw = $null }
        }

        $outLen = [BitConverter]::ToInt32($lenBuf, 0)
        if ($outLen -le 0 -or $outLen -gt 65536) {
            return @{ Ok = $false; Error = "bad_length"; ElapsedMs = $sw.ElapsedMilliseconds; Raw = $null }
        }

        $outBuf = New-Object byte[] $outLen
        $offset = 0
        $bodyDeadline = [DateTime]::UtcNow.AddMilliseconds($ResponseTimeoutMs)
        while ($offset -lt $outLen -and [DateTime]::UtcNow -lt $bodyDeadline) {
            $remaining = [Math]::Min(4096, $outLen - $offset)
            $bodyWait = $stream.BeginRead($outBuf, $offset, $remaining, $null, $null)
            $msLeft = [Math]::Max(1, ($bodyDeadline - [DateTime]::UtcNow).TotalMilliseconds)
            if (-not $bodyWait.AsyncWaitHandle.WaitOne([int]$msLeft)) { break }
            $offset += $stream.EndRead($bodyWait)
        }

        $sw.Stop()
        if ($offset -lt $outLen) {
            return @{ Ok = $false; Error = "truncated_body"; ElapsedMs = $sw.ElapsedMilliseconds; Raw = $null }
        }

        $text = [System.Text.Encoding]::UTF8.GetString($outBuf)
        $doc = $text | ConvertFrom-Json
        return @{
            Ok = $true
            Error = $null
            ElapsedMs = $sw.ElapsedMilliseconds
            Raw = $doc
        }
    }
    finally {
        if (-not $proc.HasExited) {
            try { $proc.WaitForExit(3000) } catch {}
            if (-not $proc.HasExited) { try { $proc.Kill() } catch {} }
        }
        $proc.Dispose()
    }
}

$install = Get-InstallRoot
$hostExe = Join-Path $install "BrowserBridge\Fortiva.BrowserBridge.Host.exe"

Write-Host "=== Fortiva Bridge E2E (get_status_and_matches) ===" -ForegroundColor Cyan
Write-Host "Install:  $install"
Write-Host "Fortiva:  $(if (Test-FortivaRunning) { 'running' } else { 'NOT RUNNING' })"
Write-Host "URL:      $ListUrl"
Write-Host ""

if (-not (Test-Path $hostExe)) { throw "Bridge host missing: $hostExe" }

$hostsBefore = Get-BridgeHostCount
Get-Process -Name "Fortiva.BrowserBridge.Host" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

$payload = (@{
    command = "get_status_and_matches"
    payload = @{ domain = $ListDomain; url = $ListUrl }
} | ConvertTo-Json -Compress)

$result = Invoke-NativeHostOnce -HostExe $hostExe -JsonPayload $payload -ResponseTimeoutMs 6000

Start-Sleep -Milliseconds 500
$hostsAfter = Get-BridgeHostCount

Write-Host "Response time: $($result.ElapsedMs) ms"
if ($result.Raw) {
    $status = $result.Raw.status
    $matchCount = @($result.Raw.matches).Count
    Write-Host "app_running:    $($status.appRunning)"
    Write-Host "vault_unlocked: $($status.vaultUnlocked)"
    Write-Host "error:          $($status.error)"
    Write-Host "matches:        $matchCount"
    Write-Host "fillNonce:      $(if ($result.Raw.PSObject.Properties['fillNonce']) { 'present' } else { 'none' })"
}
else {
    Write-Host "FAIL: $($result.Error)" -ForegroundColor Red
    exit 1
}

if ($hostsAfter -gt 0) {
    Write-Host "FAIL: Orphan bridge host processes after request: $hostsAfter" -ForegroundColor Red
    exit 1
}

if ($result.ElapsedMs -gt $MaxResponseMs -and $RequireReady) {
    Write-Host "FAIL: Response took $($result.ElapsedMs) ms (max $MaxResponseMs ms)" -ForegroundColor Red
    exit 1
}

if ($RequireReady) {
    if (-not (Test-FortivaRunning)) {
        Write-Host "FAIL: Fortiva.Personal.exe is not running." -ForegroundColor Red
        exit 1
    }
    if ($status.error -eq "vault_locked") {
        Write-Host "FAIL: Vault is locked. Unlock Fortiva and re-run." -ForegroundColor Red
        exit 1
    }
    if (-not $status.vaultUnlocked) {
        Write-Host "FAIL: Vault not unlocked (error=$($status.error))." -ForegroundColor Red
        exit 1
    }
    if ($matchCount -lt 1) {
        Write-Host "FAIL: Expected matches.length >= 1 for $ListUrl" -ForegroundColor Red
        Write-Host "Add a vault entry with Website URL https://$ListDomain" -ForegroundColor Yellow
        exit 1
    }
    Write-Host "PASS: get_status_and_matches in $($result.ElapsedMs) ms, $matchCount match(es), no orphan hosts." -ForegroundColor Green
    exit 0
}

if ($status.error -eq "host_unreachable" -and -not (Test-FortivaRunning)) {
    Write-Host "PASS (host_unreachable — Fortiva not running)." -ForegroundColor Green
    exit 0
}

Write-Host "PASS (response received in $($result.ElapsedMs) ms)." -ForegroundColor Green
exit 0

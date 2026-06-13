#Requires -Version 5.1
<#
.SYNOPSIS
    Validates Fortiva browser-bridge connectivity end-to-end.

.DESCRIPTION
    Tests the same path Edge uses: native messaging host -> token pipe -> credential pipe.
    Use -RequireReady after unlocking Fortiva and running Connect browser in Settings.

.EXAMPLE
    .\scripts\Test-BrowserBridge.ps1 -Iterations 30 -RequireReady
#>
param(
    [int]$Iterations = 20,
    [switch]$RequireReady,
    [string]$ListDomain = "login.ionos.co.uk",
    [string]$ListUrl = "https://login.ionos.co.uk/"
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

function Invoke-NativeHost {
    param(
        [string]$HostExe,
        [string]$JsonPayload,
        [int]$ResponseTimeoutMs = 10000
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $HostExe
    $psi.WorkingDirectory = [System.IO.Path]::GetDirectoryName($HostExe)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.CreateNoWindow = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    if (-not $proc) {
        return @{ Ok = $false; Status = "spawn_failed"; Message = "Could not start bridge host." }
    }

    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($JsonPayload)
        $header = [BitConverter]::GetBytes([int]$bytes.Length)
        $proc.StandardInput.BaseStream.Write($header, 0, 4)
        $proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $proc.StandardInput.BaseStream.Flush()

        $stream = $proc.StandardOutput.BaseStream
        $lenBuf = New-Object byte[] 4
        $headerWait = $stream.BeginRead($lenBuf, 0, 4, $null, $null)
        if (-not $headerWait.AsyncWaitHandle.WaitOne($ResponseTimeoutMs)) {
            return @{ Ok = $false; Status = "no_response"; Message = "Bridge host did not respond in ${ResponseTimeoutMs}ms." }
        }
        $read = $stream.EndRead($headerWait)
        if ($read -lt 4) {
            return @{ Ok = $false; Status = "no_response"; Message = "Bridge host returned incomplete header." }
        }

        $outLen = [BitConverter]::ToInt32($lenBuf, 0)
        if ($outLen -le 0 -or $outLen -gt 65536) {
            return @{ Ok = $false; Status = "bad_length"; Message = "Invalid response length: $outLen" }
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
        if ($offset -lt $outLen) {
            return @{ Ok = $false; Status = "truncated"; Message = "Response truncated." }
        }

        $text = [System.Text.Encoding]::UTF8.GetString($outBuf)

        $doc = $text | ConvertFrom-Json
        return @{
            Ok = [bool]$doc.ok
            Status = [string]$doc.status
            Message = [string]$doc.message
            Raw = $doc
        }
    }
    finally {
        try { if (-not $proc.HasExited) { $proc.Kill() } } catch {}
        $proc.Dispose()
    }
}

function Test-NamedPipeConnect {
    param([string]$Name, [int]$TimeoutMs = 3000)
    $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $Name, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $client.Connect($TimeoutMs)
        return $true
    }
    catch { return $false }
    finally { $client.Dispose() }
}

# --- Pre-flight ---
$install = Get-InstallRoot
$appExe = Join-Path $install "Fortiva.Personal.exe"
$hostExe = Join-Path $install "BrowserBridge\Fortiva.BrowserBridge.Host.exe"
$version = (Get-Item $appExe).VersionInfo.FileVersion
$manifest = Join-Path $env:LOCALAPPDATA "FortivaPersonal\NativeMessaging\com.fortiva.browserbridge.personal.json"

Write-Host "=== Fortiva Browser Bridge Test ===" -ForegroundColor Cyan
Write-Host "Version:     $version"
Write-Host "Install:     $install"
Write-Host "Iterations:  $Iterations"
Write-Host "Fortiva app: $(if (Test-FortivaRunning) { 'running' } else { 'NOT RUNNING' })"
Write-Host "Manifest:    $(if (Test-Path $manifest) { 'registered' } else { 'MISSING' })"
Write-Host ""

if (-not (Test-Path $hostExe)) { throw "Bridge host missing: $hostExe" }

Get-Process -Name "Fortiva.BrowserBridge.Host" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

if (-not (Test-FortivaRunning)) {
    Write-Host "WARN: Fortiva is not running. Start and unlock the app before expecting 'ready'." -ForegroundColor Yellow
    Write-Host ""
}

# --- Warmup (native host cold-start) ---
$warmup = Invoke-NativeHost -HostExe $hostExe -JsonPayload '{"command":"ping"}' -ResponseTimeoutMs 15000
if ($warmup.Status -eq "no_response") {
    Write-Host "WARN: Warmup ping had no response; continuing." -ForegroundColor Yellow
}
Start-Sleep -Milliseconds 250

# --- Run iterations ---
$pingResponded = 0
$pingReady = 0
$bridgePipeUp = 0

for ($i = 1; $i -le $Iterations; $i++) {
    $bridgeUp = Test-NamedPipeConnect -Name "Fortiva.BrowserBridge" -TimeoutMs 2000
    if ($bridgeUp) { $bridgePipeUp++ }

    $pingTimeout = if ($RequireReady) { 10000 } else { 8000 }
    $ping = @{ Status = "no_response" }
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $ping = Invoke-NativeHost -HostExe $hostExe -JsonPayload '{"command":"ping"}' -ResponseTimeoutMs $pingTimeout
        if ($ping.Status -ne "no_response") { break }
        Start-Sleep -Milliseconds 400
    }
    if ($ping.Status -in @("ready", "setup_required", "locked", "bridge_warming")) { $pingResponded++ }
    if ($ping.Status -eq "ready") { $pingReady++ }

    $bridgeHint = if ($bridgeUp) { "bridge=up" } else { "bridge=down" }
    $pingHint = "ping=$($ping.Status)"
    Write-Host ("[{0,2}/{1}] {2,-12} {3}" -f $i, $Iterations, $bridgeHint, $pingHint)
    if ($i % 10 -eq 0) { [GC]::Collect() }
    Start-Sleep -Milliseconds 50
}

Write-Host ""
Write-Host "Summary"
Write-Host "-------"
Write-Host ("Bridge pipe available:  {0,3} / {1}" -f $bridgePipeUp, $Iterations)
Write-Host ("Native host responded:  {0,3} / {1}" -f $pingResponded, $Iterations)
Write-Host ("Native ping ready:      {0,3} / {1}" -f $pingReady, $Iterations)
Write-Host ""

if ($pingResponded -lt $Iterations) {
    Write-Host "FAIL: Native host did not respond on every iteration." -ForegroundColor Red
    Write-Host "Fix:  Reinstall bridge host, reload extension, kill stray Fortiva.BrowserBridge.Host.exe processes." -ForegroundColor Red
    exit 1
}

if ($RequireReady) {
    if ($pingReady -lt $Iterations) {
        Write-Host "FAIL: Vault not ready ($pingReady/$Iterations)." -ForegroundColor Red
        Write-Host "Fix:  1) Unlock Fortiva with Windows Hello" -ForegroundColor Yellow
        Write-Host "      2) Settings -> Browser extension -> Connect browser" -ForegroundColor Yellow
        Write-Host "      3) edge://extensions -> Reload Fortiva" -ForegroundColor Yellow
        Write-Host "      4) Re-run this script" -ForegroundColor Yellow
        exit 1
    }
    if ($bridgePipeUp -lt $Iterations) {
        Write-Host "FAIL: Bridge pipe unavailable while vault reports ready." -ForegroundColor Red
        exit 1
    }

    $prepJson = (@{
        command = "prepare_fill"
        payload = @{ domain = $ListDomain; url = $ListUrl }
    } | ConvertTo-Json -Compress)
    $prep = Invoke-NativeHost -HostExe $hostExe -JsonPayload $prepJson -ResponseTimeoutMs 30000
    if ($prep.Raw) {
        $matchCount = @($prep.Raw.matches).Count
        $err = [string]$prep.Raw.error
        $status = [string]$prep.Raw.status
        Write-Host "prepare_fill: status=$status matches=$matchCount error=$err"
        if ($err -eq "no_match" -or ($matchCount -lt 1 -and $status -eq "ready")) {
            Write-Host "WARN: No vault entries matched $ListDomain (add Website URL in Fortiva)." -ForegroundColor Yellow
        }
        elseif ($err -and $err -ne "no_match") {
            Write-Host "FAIL: prepare_fill returned error=$err" -ForegroundColor Red
            exit 1
        }
    }

    Write-Host "PASS: Bridge ready on all $Iterations iterations." -ForegroundColor Green
    exit 0
}

if ($pingReady -eq 0) {
    Write-Host "PASS (host responds; vault locked or bridge not started)." -ForegroundColor Green
    Write-Host "Re-run with -RequireReady after unlocking to validate full path." -ForegroundColor Yellow
    exit 0
}

if ($pingReady -lt $Iterations) {
    Write-Host "WARN: Partial readiness ($pingReady/$Iterations)." -ForegroundColor Yellow
    exit 1
}

Write-Host "PASS" -ForegroundColor Green
exit 0

#Requires -Version 5.1
param(
    [string]$Domain = "login.ionos.co.uk",
    [string]$Url = "https://login.ionos.co.uk/",
    [int]$StressIterations = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$install = Join-Path $env:LOCALAPPDATA "Programs\icmclab studio\Fortiva Personal"
$hostExe = Join-Path $install "BrowserBridge\Fortiva.BrowserBridge.Host.exe"
$appExe = Join-Path $install "Fortiva.Personal.exe"
$manifest = Join-Path $env:LOCALAPPDATA "FortivaPersonal\NativeMessaging\com.fortiva.browserbridge.personal.json"
$vault = Join-Path $env:APPDATA "Fortiva\vault.fva"
$hello = Join-Path $env:APPDATA "Fortiva\hello.keyprotect"
$stagingExt = Join-Path $env:LOCALAPPDATA "FortivaPersonal\extension\manifest.json"

$results = New-Object System.Collections.Generic.List[object]
$warnings = New-Object System.Collections.Generic.List[string]

function Add-Result([string]$Name, [bool]$Pass, [string]$Detail, [switch]$WarnOnly) {
    $results.Add([pscustomobject]@{ Test = $Name; Pass = $Pass; WarnOnly = [bool]$WarnOnly; Detail = $Detail })
    if ($WarnOnly -and -not $Pass) { $warnings.Add("$Name : $Detail") }
    $color = if ($Pass) { "Green" } elseif ($WarnOnly) { "Yellow" } else { "Red" }
    $mark = if ($Pass) { "PASS" } elseif ($WarnOnly) { "WARN" } else { "FAIL" }
    Write-Host ("[{0}] {1,-42} {2}" -f $mark, $Name, $Detail) -ForegroundColor $color
}

function Start-ExecuteFillBackground {
    param([string]$JsonPayload, [int]$TimeoutMs = 140000)
    $exe = $hostExe
    $workDir = Split-Path $exe -Parent
    return Start-Job -ScriptBlock {
        param($Exe, $WorkDir, $Payload, $Timeout)
        if (-not (Test-Path $Exe)) { return $null }
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $Exe
        $psi.WorkingDirectory = $WorkDir
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.CreateNoWindow = $true
        $proc = [System.Diagnostics.Process]::Start($psi)
        if (-not $proc) { return $null }
        try {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($Payload)
            $proc.StandardInput.BaseStream.Write([BitConverter]::GetBytes([int]$bytes.Length), 0, 4)
            $proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
            $proc.StandardInput.Close()
            $lenBuf = New-Object byte[] 4
            $wait = $proc.StandardOutput.BaseStream.BeginRead($lenBuf, 0, 4, $null, $null)
            if (-not $wait.AsyncWaitHandle.WaitOne($Timeout)) { return $null }
            $proc.StandardOutput.BaseStream.EndRead($wait) | Out-Null
            $outLen = [BitConverter]::ToInt32($lenBuf, 0)
            if ($outLen -le 0 -or $outLen -gt 65536) { return $null }
            $outBuf = New-Object byte[] $outLen
            $offset = 0
            while ($offset -lt $outLen) { $offset += $proc.StandardOutput.BaseStream.Read($outBuf, $offset, $outLen - $offset) }
            $proc.WaitForExit(5000) | Out-Null
            try { [System.Text.Encoding]::UTF8.GetString($outBuf) | ConvertFrom-Json } catch { $null }
        }
        finally {
            if (-not $proc.HasExited) {
                try { $proc.Kill() } catch { }
            }
            $proc.Dispose()
        }
    } -ArgumentList $exe, $workDir, $JsonPayload, $TimeoutMs
}

function Invoke-HostJson {
    param([string]$JsonPayload, [int]$TimeoutMs = 120000)
    if (-not (Test-Path $hostExe)) { return $null }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $hostExe
    $psi.WorkingDirectory = Split-Path $hostExe -Parent
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.CreateNoWindow = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($JsonPayload)
    $proc.StandardInput.BaseStream.Write([BitConverter]::GetBytes([int]$bytes.Length), 0, 4)
    $proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $proc.StandardInput.Close()
    $lenBuf = New-Object byte[] 4
    $wait = $proc.StandardOutput.BaseStream.BeginRead($lenBuf, 0, 4, $null, $null)
    if (-not $wait.AsyncWaitHandle.WaitOne($TimeoutMs)) { return $null }
    $proc.StandardOutput.BaseStream.EndRead($wait) | Out-Null
    $outLen = [BitConverter]::ToInt32($lenBuf, 0)
    if ($outLen -le 0 -or $outLen -gt 65536) { return $null }
    $outBuf = New-Object byte[] $outLen
    $offset = 0
    while ($offset -lt $outLen) { $offset += $proc.StandardOutput.BaseStream.Read($outBuf, $offset, $outLen - $offset) }
    $proc.WaitForExit(5000) | Out-Null
    try { [System.Text.Encoding]::UTF8.GetString($outBuf) | ConvertFrom-Json } catch { $null }
}

function Get-JsonStatus($obj) {
    if ($null -eq $obj) { return "null" }
    if ($obj.PSObject.Properties.Match("status").Count -gt 0) { return [string]$obj.status }
    return "unknown"
}

function Test-Pipe([string]$Name) {
    $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $Name, [System.IO.Pipes.PipeDirection]::InOut)
    try { $client.Connect(2000); return $true } catch { return $false } finally { $client.Dispose() }
}

function Wait-PingStatus([string]$Want, [int]$MaxSeconds = 45) {
    for ($i = 0; $i -lt $MaxSeconds; $i++) {
        $p = Invoke-HostJson '{"command":"ping"}' 10000
        if ($p -and $p.status -eq $Want) { return $p }
        Start-Sleep -Seconds 1
    }
    return $null
}

function Stop-Fortiva {
    Get-Process Fortiva.Personal, Fortiva.BrowserBridge.Host -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

Write-Host ""
Write-Host "Fortiva Extension Full Test Matrix" -ForegroundColor Cyan
if (Test-Path $appExe) { Write-Host "Version: $((Get-Item $appExe).VersionInfo.FileVersion)" }
Write-Host ""

Add-Result "Bridge host exists" (Test-Path $hostExe) $hostExe
Add-Result "Fortiva app exists" (Test-Path $appExe) $appExe
Add-Result "Native manifest registered" (Test-Path $manifest) $manifest
Add-Result "Vault file exists" (Test-Path $vault) $vault
Add-Result "Windows Hello key exists" (Test-Path $hello) $(if (Test-Path $hello) { $hello } else { "Missing - master password still works" }) -WarnOnly

if (Test-Path $manifest) {
    $m = Get-Content $manifest -Raw | ConvertFrom-Json
    Add-Result "Extension ID in manifest" ($m.allowed_origins[0] -match "llkpcnbhmhpenahlcdnbbfmkdfkgnpnj") $m.allowed_origins[0]
}

$extVer = if (Test-Path $stagingExt) { (Get-Content $stagingExt -Raw | ConvertFrom-Json).version } else { "" }
$appVer = if (Test-Path $appExe) { (Get-Item $appExe).VersionInfo.FileVersion } else { "" }
if ($extVer -and $appVer) {
    $extShort = ($extVer -split '\.')[0..2] -join '.'
    $appShort = ($appVer -split '\.')[0..2] -join '.'
    Add-Result "Extension/app version aligned" ($extShort -eq $appShort) "ext=$extVer app=$appVer"
}

# --- Cold start ---
Stop-Fortiva

$sw = [Diagnostics.Stopwatch]::StartNew()
$ping = Invoke-HostJson '{"command":"ping"}' 10000
$sw.Stop()
Add-Result "Cold ping responds" ($null -ne $ping) ("{0}ms status={1}" -f $sw.ElapsedMilliseconds, (Get-JsonStatus $ping))

$sw.Restart()
$prep = Invoke-HostJson (@{ command = "prepare_fill"; payload = @{ domain = $Domain; url = $Url } } | ConvertTo-Json -Compress) 8000
$sw.Stop()
Add-Result "Cold prepare_fill fast" ($sw.ElapsedMilliseconds -lt 5000) ("{0}ms status={1}" -f $sw.ElapsedMilliseconds, (Get-JsonStatus $prep))

$fillPayload = (@{ command = "execute_fill"; payload = @{ domain = $Domain; url = $Url } } | ConvertTo-Json -Compress)
$launchJob = Start-ExecuteFillBackground $fillPayload 140000

$launchSec = $null
for ($i = 1; $i -le 45; $i++) {
    if (Get-Process Fortiva.Personal -ErrorAction SilentlyContinue) { $launchSec = $i; break }
    Start-Sleep -Seconds 1
}
Add-Result "Cold execute_fill launches Fortiva" ($null -ne $launchSec) $(if ($launchSec) { "launched at ${launchSec}s" } else { "never launched" })

# Wait for unlock broker (app must finish booting)
$lockedPing = Wait-PingStatus "locked" 45
Add-Result "App reaches locked ping after launch" ($null -ne $lockedPing) $(if ($lockedPing) { "locked" } else { "timeout" })

if ($lockedPing) {
    $unlockPipe = Test-Pipe "Fortiva.Bridge.UnlockRequest"
    Add-Result "Unlock pipe up when locked" $unlockPipe "Fortiva.Bridge.UnlockRequest"

    $sw.Restart()
    $prepLocked = Invoke-HostJson (@{ command = "prepare_fill"; payload = @{ domain = $Domain; url = $Url } } | ConvertTo-Json -Compress) 8000
    $sw.Stop()
    $prepLockedOk = ($sw.ElapsedMilliseconds -lt 5000) -and ((Get-JsonStatus $prepLocked) -eq "locked")
    Add-Result "Locked prepare_fill fast preview" $prepLockedOk ("{0}ms status={1}" -f $sw.ElapsedMilliseconds, (Get-JsonStatus $prepLocked))

    $stable = 0
    for ($i = 1; $i -le 5; $i++) {
        $p = Invoke-HostJson '{"command":"ping"}' 10000
        if ($p.status -eq "locked") { $stable++ }
        Start-Sleep -Milliseconds 200
    }
    Add-Result "Locked ping stable 5/5" ($stable -eq 5) ("{0}/5 locked" -f $stable)

    Stop-Job $launchJob -ErrorAction SilentlyContinue | Out-Null
    Remove-Job $launchJob -Force -ErrorAction SilentlyContinue
    Get-Process Fortiva.BrowserBridge.Host -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    $fill = Invoke-HostJson (@{ command = "execute_fill"; payload = @{ domain = $Domain; url = $Url } } | ConvertTo-Json -Compress) 140000
    $fillErr = if ($null -eq $fill) { "no response" } elseif ($fill.PSObject.Properties.Match('error').Count -gt 0) { $fill.error } else { "ok" }
    $fillOk = ($null -ne $fill) -and ($fillErr -in @("locked", "cancelled", "no_match", "ok", "setup_required"))
    Add-Result "execute_fill responds while locked" $fillOk ("error={0}" -f $fillErr)
} else {
    Stop-Job $launchJob -ErrorAction SilentlyContinue | Out-Null
    Remove-Job $launchJob -Force -ErrorAction SilentlyContinue
    Add-Result "Unlock pipe up when locked" $false "skipped - app not locked"
    Add-Result "Locked prepare_fill fast preview" $false "skipped"
    Add-Result "Locked ping stable 5/5" $false "skipped"
    Add-Result "execute_fill responds while locked" $false "skipped"
}

# --- Stress ---
Get-Process Fortiva.BrowserBridge.Host -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300
$stressOk = 0
for ($i = 1; $i -le $StressIterations; $i++) {
    $p = Invoke-HostJson '{"command":"ping"}' 12000
    if ($p -and $p.status) { $stressOk++ }
    Start-Sleep -Milliseconds 80
}
Add-Result "Ping stress test" ($stressOk -eq $StressIterations) ("{0}/{1} responded" -f $stressOk, $StressIterations)

# --- Ready path ---
$readyPing = Invoke-HostJson '{"command":"ping"}' 10000
if ((Get-JsonStatus $readyPing) -eq "ready") {
    $prepReady = Invoke-HostJson (@{ command = "prepare_fill"; payload = @{ domain = $Domain; url = $Url } } | ConvertTo-Json -Compress) 30000
    $matchCount = 0
    if ($prepReady -and $prepReady.PSObject.Properties.Match('matches').Count -gt 0 -and $prepReady.matches) {
        $matchCount = @($prepReady.matches).Count
    }
    Add-Result "Ready bridge pipe up" (Test-Pipe "Fortiva.BrowserBridge") "Fortiva.BrowserBridge"
    Add-Result "Ready prepare_fill IONOS matches" ($matchCount -gt 0) ("matches={0}" -f $matchCount)
} else {
    Add-Result "Ready path (vault unlocked)" $false ("status={0} - run Test-ExtensionRequireReady.ps1" -f (Get-JsonStatus $readyPing)) -WarnOnly
}

$hardFail = @($results | Where-Object { -not $_.Pass -and -not $_.WarnOnly }).Count
$warn = @($results | Where-Object { -not $_.Pass -and $_.WarnOnly }).Count
$passed = @($results | Where-Object { $_.Pass }).Count
Write-Host ""
Write-Host ("SUMMARY: {0} passed, {1} hard fail, {2} warn, {3} total" -f $passed, $hardFail, $warn, $results.Count) -ForegroundColor $(if ($hardFail -eq 0) { "Green" } else { "Red" })

if ($hardFail -gt 0) { exit 1 }
exit 0

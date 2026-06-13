#Requires -Version 5.1
param(
    [string]$Domain = "login.ionos.co.uk",
    [string]$Url = "https://login.ionos.co.uk/"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$install = Join-Path $env:LOCALAPPDATA "Programs\icmclab studio\Fortiva Personal"
$hostExe = Join-Path $install "BrowserBridge\Fortiva.BrowserBridge.Host.exe"

function Invoke-HostJson {
    param([string]$JsonPayload, [int]$TimeoutMs = 120000)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $hostExe
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
    $outBuf = New-Object byte[] $outLen
    $offset = 0
    while ($offset -lt $outLen) { $offset += $proc.StandardOutput.BaseStream.Read($outBuf, $offset, $outLen - $offset) }
    $proc.WaitForExit(3000) | Out-Null
    [System.Text.Encoding]::UTF8.GetString($outBuf)
}

Write-Host "=== prepare_fill ===" -ForegroundColor Cyan
$prepJson = (@{ command = "prepare_fill"; payload = @{ domain = $Domain; url = $Url } } | ConvertTo-Json -Compress)
$prep = Invoke-HostJson $prepJson
Write-Host $prep

Write-Host "`n=== execute_fill ===" -ForegroundColor Cyan
$fillJson = (@{ command = "execute_fill"; payload = @{ domain = $Domain; url = $Url } } | ConvertTo-Json -Compress)
$fill = Invoke-HostJson $fillJson
Write-Host $fill

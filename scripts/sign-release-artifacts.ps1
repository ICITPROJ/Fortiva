param(
    [ValidateSet('Published', 'Installers', 'All')]
    [string]$Stage = 'All'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Get-SignToolPath {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path $kitsRoot)) {
        throw 'Windows SDK signtool.exe not found. Install Windows SDK on the release runner.'
    }

    Get-ChildItem $kitsRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object {
            $candidate = Join-Path $_.FullName 'x64\signtool.exe'
            if (Test-Path $candidate) { return $candidate }
        }

    throw 'signtool.exe not found under Windows Kits\10\bin.'
}

function Get-FilesToSign {
    param([string]$Phase)

    switch ($Phase) {
        'Published' {
            Get-ChildItem (Join-Path $root 'dist') -Recurse -Filter '*.exe' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\installers\\' }
        }
        'Installers' {
            Get-ChildItem (Join-Path $root 'dist\installers') -Filter '*-Setup.exe' -File -ErrorAction SilentlyContinue
        }
    }
}

$pfxB64 = $env:CODESIGN_PFX_BASE64
$pfxPass = $env:CODESIGN_PFX_PASSWORD
if ([string]::IsNullOrWhiteSpace($pfxB64) -or [string]::IsNullOrWhiteSpace($pfxPass)) {
    Write-Warning 'CODESIGN_PFX_BASE64 / CODESIGN_PFX_PASSWORD not set — skipping Authenticode signing.'
    exit 0
}

$signtool = Get-SignToolPath
$pfxPath = Join-Path $env:RUNNER_TEMP 'fortiva-codesign.pfx'
if (-not $env:RUNNER_TEMP) { $pfxPath = Join-Path ([IO.Path]::GetTempPath()) 'fortiva-codesign.pfx' }

[IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($pfxB64.Trim()))
$timestamp = if ($env:CODESIGN_TIMESTAMP_URL) { $env:CODESIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }

$phases = if ($Stage -eq 'All') { @('Published', 'Installers') } else { @($Stage) }
$signed = 0

foreach ($phase in $phases) {
    foreach ($file in @(Get-FilesToSign -Phase $phase)) {
        Write-Host "Signing $($file.FullName)..." -ForegroundColor Cyan
        & $signtool sign `
            /f $pfxPath `
            /p $pfxPass `
            /tr $timestamp `
            /td sha256 `
            /fd sha256 `
            $file.FullName
        if ($LASTEXITCODE -ne 0) { throw "signtool failed for $($file.FullName)" }
        $signed++
    }
}

Write-Host "Signed $signed file(s)." -ForegroundColor Green

try { Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue } catch { }

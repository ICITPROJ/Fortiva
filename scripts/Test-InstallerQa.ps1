#Requires -Version 5.1
<#
.SYNOPSIS
    CI/release gate: validate dist layout, bridge hash sidecar, and Personal installer build.
.DESCRIPTION
    Expects build-release.ps1 to have populated dist/. Does not install or launch the app.
#>
param(
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Resolve-IsccPath {
    foreach ($candidate in @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
        )) {
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $props = Join-Path $root "Directory.Build.props"
    $m = Select-String -Path $props -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $m) { Fail "Could not read Version from Directory.Build.props" }
    $Version = $m.Matches[0].Groups[1].Value
}

Write-Host "Installer QA for version $Version" -ForegroundColor Cyan

& (Join-Path $root "scripts\Test-ExtensionManifest.ps1")
$manifestExit = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
if ($manifestExit -ne 0) { exit $manifestExit }

$personalExe = Join-Path $root "dist\Fortiva.Personal\Fortiva.Personal.exe"
$bridgeExe = Join-Path $root "dist\Fortiva.Personal\BrowserBridge\Fortiva.BrowserBridge.Host.exe"
$sidecar = Join-Path $root "dist\Fortiva.Personal\BrowserBridge\bridge-host.sha256"
$extManifest = Join-Path $root "dist\Fortiva.Personal\extension\manifest.json"

foreach ($path in @($personalExe, $bridgeExe, $extManifest)) {
    if (-not (Test-Path $path)) { Fail "Missing release artifact: $path" }
}

if (-not (Test-Path $sidecar)) {
    Fail "Missing bridge-host.sha256 - run build-release hash pin step"
}

$expected = (Get-Content $sidecar -Raw).Trim().ToLowerInvariant()
$actual = (Get-FileHash $bridgeExe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -ne $actual) {
    Fail "bridge-host.sha256 mismatch (sidecar=$expected computed=$actual)"
}

$iscc = Resolve-IsccPath
if (-not $iscc) { Fail "ISCC.exe not found - install Inno Setup 6" }

Write-Host "Fetching installer prerequisites..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "scripts\fetch-installer-prerequisites.ps1")
$prereqExit = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
if ($prereqExit -ne 0) { Fail "fetch-installer-prerequisites failed with exit $prereqExit" }
foreach ($req in @('MicrosoftEdgeWebview2Setup.exe', 'vc_redist.x64.exe')) {
    $p = Join-Path $root "packaging\prerequisites\$req"
    if (-not (Test-Path $p)) { Fail "Missing prerequisite: $p" }
}

& powershell -ExecutionPolicy Bypass -File (Join-Path $root "scripts\ensure-extension-key.ps1") | Out-Null
$extensionId = & powershell -ExecutionPolicy Bypass -File (Join-Path $root "scripts\compute-extension-id.ps1")
if ($extensionId -match 'REPLACE|NOT_SET' -or $extensionId.Length -ne 32) {
    Fail "Invalid extension ID: '$extensionId'"
}

$iss = Join-Path $root "packaging\installer\FortivaPersonal.iss"
& $iscc "/DAppVersion=$Version" $iss
$isccExit = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
if ($isccExit -ne 0) { Fail "ISCC failed with exit $isccExit" }

$installer = Join-Path $root "dist\installers\FortivaPersonal-$Version-Setup.exe"
if (-not (Test-Path $installer)) {
    $fallback = Get-ChildItem (Join-Path $root "dist\installers\FortivaPersonal-*-Setup.exe") -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($fallback) { $installer = $fallback.FullName }
}
if (-not (Test-Path $installer)) { Fail "Personal installer not produced" }

$sizeMb = [math]::Round((Get-Item $installer).Length / 1MB, 1)
if ($sizeMb -lt 20) { Fail "Installer too small ($sizeMb MB): $installer" }

Write-Host "Installer QA passed ($sizeMb MB): $installer" -ForegroundColor Green
Write-Host "Bridge hash sidecar OK" -ForegroundColor Green
exit 0

param(
    [string]$Version = ""
)

$root = $PSScriptRoot
$iscc = $null
foreach ($candidate in @(
        "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
    if (Test-Path $candidate) { $iscc = $candidate; break }
}
if (-not $iscc) {
    Write-Error "ISCC.exe not found. Install Inno Setup 6 (local or choco install innosetup)."
    exit 1
}
$licenseTool = Join-Path $root "dist\LicenseTool\Fortiva.LicenseTool.exe"
if (-not (Test-Path $licenseTool)) {
    Write-Error "LicenseTool missing at $licenseTool - run build-release.ps1 first"
    exit 1
}

$bridgeExe = Join-Path $root "dist\BrowserBridge\Fortiva.BrowserBridge.Host.exe"
if (-not (Test-Path $bridgeExe)) {
    Write-Error "BrowserBridge host missing at $bridgeExe - run build-release.ps1 first"
    exit 1
}

$extManifest = Join-Path $root "extension\manifest.json"
if (-not (Test-Path $extManifest)) {
    Write-Error "Browser extension missing at $extManifest"
    exit 1
}

$scripts = @('FortivaPersonal', 'FortivaEnterprise', 'FortivaAdmin')
$anyFail = $false

foreach ($s in $scripts) {
    $iss = Join-Path $root "packaging\installer\$s.iss"
    Write-Host "Building $s installer..."
    $isccArgs = @()
    if ($Version) { $isccArgs += "/DAppVersion=$Version" }
    & $iscc @isccArgs $iss 2>&1 | Where-Object { $_ -match 'Successful|error|Error|warning' }
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$s FAILED (exit $LASTEXITCODE)"
        $anyFail = $true
    } else {
        Write-Host "$s installer OK"
    }
}

if ($anyFail) {
    Write-Error "One or more installers failed."
    exit 1
}

Write-Host ""
Write-Host "All installers built."
Get-ChildItem (Join-Path $root "dist\installers") -Filter "*.exe" |
    Select-Object Name, @{N='Size';E={'{0:F1} MB' -f ($_.Length/1MB)}} |
    Format-Table -AutoSize

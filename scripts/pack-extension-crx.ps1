# Packs extension/ into a signed CRX and writes fortiva-extension-updates.xml for Enterprise force-install.
param(
    [string]$Version,
    [string]$ExtensionDir = (Join-Path $PSScriptRoot '..\extension'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist\extension'),
    [string]$PrivateKeyPath = $env:EXTENSION_PRIVATE_KEY_PEM,
    [string]$CrxUrlBase = "https://github.com/ICITPROJ/Fortiva/releases/download"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $PrivateKeyPath) {
    $defaultKey = Join-Path $root 'packaging\extension-keys\fortiva-extension.pem'
    if (Test-Path -LiteralPath $defaultKey) { $PrivateKeyPath = $defaultKey }
}

$manifestPath = Join-Path $ExtensionDir 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Extension manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (-not $Version) {
    $Version = [string]$manifest.version
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'Extension version missing from manifest.json and -Version not supplied.'
}

$extensionId = & (Join-Path $PSScriptRoot 'compute-extension-id.ps1') -ManifestPath $manifestPath
if ($extensionId -ne 'llkpcnbhmhpenahlcdnbbfmkdfkgnpnj') {
    Write-Warning "Extension ID $extensionId differs from BrowserExtensionConstants.StableExtensionId."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$crxName = 'FortivaAutofill.crx'
$crxPath = Join-Path $OutputDir $crxName
$updatesPath = Join-Path $OutputDir 'fortiva-extension-updates.xml'

if (-not $PrivateKeyPath -or -not (Test-Path -LiteralPath $PrivateKeyPath)) {
    Write-Warning @"
EXTENSION PRIVATE KEY NOT FOUND — skipping CRX pack.
Enterprise ExtensionInstallForcelist requires a signed CRX at release time.
Place PEM at packaging\extension-keys\fortiva-extension.pem or set EXTENSION_PRIVATE_KEY_PEM.
See packaging\extension-keys\README.md
"@
    exit 0
}

function Resolve-ChromeExecutable {
    $candidates = @(
        (Join-Path ${env:ProgramFiles} 'Google\Chrome\Application\chrome.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
        (Join-Path $env:LOCALAPPDATA 'Google\Chrome\Application\chrome.exe')
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) { return $path }
    }
    return $null
}

$chrome = Resolve-ChromeExecutable
if (-not $chrome) {
    Write-Warning 'Google Chrome not found — cannot pack CRX (chrome --pack-extension required). Skipping.'
    exit 0
}

if (Test-Path -LiteralPath $crxPath) { Remove-Item -LiteralPath $crxPath -Force }

$packDir = Join-Path $env:TEMP ("fortiva-ext-pack-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $packDir | Out-Null
try {
    $args = @(
        "--pack-extension=$ExtensionDir",
        "--pack-extension-key=$PrivateKeyPath",
        "--no-message-box"
    )
    $proc = Start-Process -FilePath $chrome -ArgumentList $args -PassThru -Wait -WindowStyle Hidden
    if ($proc.ExitCode -ne 0) {
        throw "chrome --pack-extension failed with exit code $($proc.ExitCode)"
    }

    $packed = Join-Path $ExtensionDir 'extension.crx'
    if (-not (Test-Path -LiteralPath $packed)) {
        throw "Expected packed CRX at $packed"
    }

    Move-Item -LiteralPath $packed -Destination $crxPath -Force
    if (Test-Path -LiteralPath (Join-Path $ExtensionDir 'extension.pem')) {
        Remove-Item -LiteralPath (Join-Path $ExtensionDir 'extension.pem') -Force -ErrorAction SilentlyContinue
    }
}
finally {
    if (Test-Path -LiteralPath $packDir) { Remove-Item -LiteralPath $packDir -Recurse -Force -ErrorAction SilentlyContinue }
}

$crxUrl = "$CrxUrlBase/v$Version/$crxName"
$templatePath = Join-Path $root 'packaging\extension\fortiva-extension-updates.xml.template'
$template = Get-Content -LiteralPath $templatePath -Raw
$updatesXml = $template.Replace('__CRX_URL__', $crxUrl).Replace('__VERSION__', $Version)
Set-Content -LiteralPath $updatesPath -Value $updatesXml -Encoding UTF8 -NoNewline

Write-Host "Packed $crxPath" -ForegroundColor Green
Write-Host "Wrote $updatesPath (codebase=$crxUrl version=$Version)" -ForegroundColor Green
Write-Host "Extension ID: $extensionId" -ForegroundColor Cyan

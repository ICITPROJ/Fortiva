#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Full release build for Fortiva (Personal, Enterprise, Admin).
    Uses VS MSBuild for XAML compilation, dotnet publish for self-contained output,
    then makepri.exe to generate resources.pri.
#>
param(
    [string]$Version = ""
)

Set-StrictMode -Off
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }
}
function Find-MakePri {
    $cached = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" `
        -Recurse -Filter "makepri.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\makepri\.exe$' } |
        Sort-Object FullName |
        Select-Object -Last 1
    if ($cached) { return $cached.FullName }
    return $null
}

function Stop-FortivaDistProcesses {
    param([string]$ExeName)
    $procs = @(Get-Process -Name $ExeName -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return }
    Write-Host "  Stopping $($procs.Count) running $ExeName process(es) — dist output is locked while the app runs." -ForegroundColor Yellow
    $procs | Stop-Process -Force
    Start-Sleep -Milliseconds 750
}

function Assert-SelfContainedPublish {
    param(
        [string]$DistDir,
        [string]$ExeBaseName
    )
    $runtimeConfig = Join-Path $DistDir "$ExeBaseName.runtimeconfig.json"
    if (-not (Test-Path $runtimeConfig)) {
        throw "Missing $ExeBaseName.runtimeconfig.json in $DistDir"
    }
    $json = Get-Content $runtimeConfig -Raw
    if ($json -match '"framework"\s*:') {
        throw "$ExeBaseName publish is framework-dependent (runtimeconfig has 'framework' not 'includedFrameworks'). Users will be prompted to install .NET Desktop Runtime. Re-publish with --self-contained."
    }
    if ($json -notmatch 'includedFrameworks') {
        throw "$ExeBaseName publish is not self-contained (no includedFrameworks in runtimeconfig)."
    }
    if (-not (Test-Path (Join-Path $DistDir "hostfxr.dll"))) {
        throw "$ExeBaseName publish is missing hostfxr.dll — not a complete self-contained layout."
    }
}

$makepri = Find-MakePri
if (-not $makepri) {
    Write-Host "makepri.exe not in NuGet cache — restoring WinUI project packages..."
    $bootstrapProj = Join-Path $root "src\Fortiva.Personal\Fortiva.Personal.csproj"
    & dotnet restore $bootstrapProj -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --nologo -q
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed (needed for makepri.exe)" }
    $makepri = Find-MakePri
}

if (-not $msbuild -or -not (Test-Path $msbuild)) { throw "MSBuild not found (install VS Build Tools or run on a VS machine)" }
if (-not $makepri)              { throw "makepri.exe not found in NuGet cache after restore" }

Write-Host "==> MSBuild : $msbuild" -ForegroundColor Cyan
Write-Host "==> makepri : $makepri"  -ForegroundColor Cyan

$msbuildVersionArgs = @()
$dotnetVersionArgs = @()
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+') { throw "Invalid version: $Version (expected major.minor.patch)" }
    $assemblyVersion = '{0}.0' -f $Version
    $msbuildVersionArgs = @(
        "/p:Version=$Version",
        "/p:AssemblyVersion=$assemblyVersion",
        "/p:FileVersion=$assemblyVersion"
    )
    $dotnetVersionArgs = @(
        "-p:Version=$Version",
        "-p:AssemblyVersion=$assemblyVersion",
        "-p:FileVersion=$assemblyVersion"
    )
    Write-Host "==> Version : $Version (assembly $assemblyVersion)" -ForegroundColor Cyan
}

$apps = @(
    @{ Name="Fortiva.Personal";   Proj="src\Fortiva.Personal\Fortiva.Personal.csproj";     Out="dist\Fortiva.Personal"   },
    @{ Name="Fortiva.Enterprise"; Proj="src\Fortiva.Enterprise\Fortiva.Enterprise.csproj"; Out="dist\Fortiva.Enterprise" },
    @{ Name="Fortiva.Admin";      Proj="src\Fortiva.Admin\Fortiva.Admin.csproj";           Out="dist\Fortiva.Admin"      }
)

foreach ($app in $apps) {
    $proj     = Join-Path $root $app.Proj
    $distDir  = Join-Path $root $app.Out
    $name     = $app.Name

    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host "  Building $name" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green

    # ── Step 1: Restore ──────────────────────────────────────────────────────
    Write-Host "`n[1/4] Restore..."
    & dotnet restore $proj -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --nologo -q
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for $name" }

    # ── Step 2: Build with VS MSBuild (generates .g.cs + .xbf via in-proc task) ──
    Write-Host "[2/4] Build (VS MSBuild)..."
    $buildArgs = @(
        $proj, '/t:Build',
        '/p:Configuration=Release', '/p:Platform=x64', '/p:RuntimeIdentifier=win-x64',
        '/p:SelfContained=true', '/p:WindowsAppSDKSelfContained=true',
        '/m', '/nologo', '/verbosity:minimal'
    ) + $msbuildVersionArgs
    & $msbuild @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "MSBuild Build failed for $name" }

    # ── Step 3: Publish (copies EXE + DLLs + WinUI runtime) ─────────────────
    Write-Host "[3/4] Publish (dotnet)..."
    Stop-FortivaDistProcesses -ExeName $name
    $publishArgs = @(
        'publish', $proj,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained',
        '-o', $distDir,
        '-p:Platform=x64',
        '--nologo',
        '--no-build'
    ) + $dotnetVersionArgs
    & dotnet @publishArgs 2>&1 |
        Where-Object { $_ -notmatch "^\s*$" } |
        ForEach-Object { Write-Host "  $_" }
    # --no-build skips re-building; just copies artifacts to PublishDir
    $publishExitCode = $LASTEXITCODE
    if ($publishExitCode -ne 0) {
        throw "Publish failed for $name (close any running $name window and retry, or re-run build-release.ps1)"
    }

    Assert-SelfContainedPublish -DistDir $distDir -ExeBaseName $name

    # ── Step 4: Generate resources.pri (clean, reliable) ────────────────────
    Write-Host "[4/4] Generate resources.pri..."

    # Find intermediate path for XBF files
    $projDir  = Split-Path $proj
    $objXbfBase = "$projDir\obj\x64\Release\net8.0-windows10.0.19041.0\win-x64"

    $tempDir = Join-Path $env:TEMP "FortivaAppPri\$name"
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force "$tempDir\Pages" | Out-Null
    New-Item -ItemType Directory -Force "$tempDir\Admin"  | Out-Null

    # Copy XBF from intermediate (most reliable source after VS MSBuild)
    if (Test-Path "$objXbfBase\App.xbf") {
        Get-ChildItem "$objXbfBase" -Recurse -Filter "*.xbf" | ForEach-Object {
            $rel  = $_.FullName.Substring($objXbfBase.Length).TrimStart('\')
            $dest = Join-Path $tempDir $rel
            New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
            Copy-Item $_.FullName $dest -Force
        }
    } else {
        # Fallback: use XBF files already in distDir
        Get-ChildItem $distDir -Filter "*.xbf" | Copy-Item -Destination $tempDir -Force
        if (Test-Path "$distDir\Pages") { Copy-Item "$distDir\Pages\*.xbf" "$tempDir\Pages\" -Force -ErrorAction SilentlyContinue }
        if (Test-Path "$distDir\Admin")  { Copy-Item "$distDir\Admin\*.xbf"  "$tempDir\Admin\"  -Force -ErrorAction SilentlyContinue }
    }

    # Copy WinUI PRI files from the publish output
    foreach ($pri in @("Microsoft.UI.Xaml.Controls.pri", "Microsoft.UI.pri")) {
        $src = Join-Path $distDir $pri
        if (Test-Path $src) { Copy-Item $src $tempDir -Force }
    }

    $cfg    = Join-Path $tempDir "priconfig.xml"
    $priOut = Join-Path $distDir "resources.pri"

    & $makepri createconfig /cf $cfg /dq Language-en-US /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makepri createconfig failed for $name" }

    & $makepri new /pr $tempDir /cf $cfg /o /of $priOut /in $name 2>&1 |
        Select-String "Resource Map Name|Named Resources|Successfully" |
        ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "makepri new failed for $name" }

    # Also ensure XBF files are in publish dir at correct relative paths
    Get-ChildItem "$objXbfBase" -Recurse -Filter "*.xbf" | ForEach-Object {
        $rel  = $_.FullName.Substring($objXbfBase.Length).TrimStart('\')
        $dest = Join-Path $distDir $rel
        New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
        Copy-Item $_.FullName $dest -Force
    }

    # Remove the stale AppPriSource subdir from dist if it crept in
    $stale = Join-Path $distDir "AppPriSource"
    if (Test-Path $stale) { Remove-Item -Recurse -Force $stale }

    if (Test-Path $priOut) {
        Write-Host "  resources.pri generated ($([Math]::Round((Get-Item $priOut).Length/1KB,1)) KB)" -ForegroundColor Green
    } else {
        Write-Warning "  resources.pri NOT generated for $name"
    }

    Write-Host "  Done: $distDir" -ForegroundColor Green
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  All builds complete." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# ── Browser bridge native-messaging host (Chrome/Edge autofill) ─────────────
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Building Fortiva.BrowserBridge.Host" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$bridgeProj = Join-Path $root "src\Fortiva.BrowserBridge.Host\Fortiva.BrowserBridge.Host.csproj"
$bridgeOut  = Join-Path $root "dist\BrowserBridge"

$bridgePublishArgs = @(
    'publish', $bridgeProj,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained',
    '-o', $bridgeOut,
    '-p:Platform=x64',
    '--nologo'
) + $dotnetVersionArgs
& dotnet @bridgePublishArgs
if ($LASTEXITCODE -ne 0) { throw "BrowserBridge publish failed" }
Assert-SelfContainedPublish -DistDir $bridgeOut -ExeBaseName "Fortiva.BrowserBridge.Host"
if (-not (Test-Path (Join-Path $bridgeOut "Fortiva.BrowserBridge.Host.exe"))) {
    throw "Fortiva.BrowserBridge.Host.exe missing after publish"
}
Write-Host "  Done: $bridgeOut" -ForegroundColor Green

# ── License tool (Admin Console bundle + dev signing) ─────────────────────
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Building Fortiva.LicenseTool" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$licenseProj = Join-Path $root "src\Fortiva.LicenseTool\Fortiva.LicenseTool.csproj"
$licenseOut  = Join-Path $root "dist\LicenseTool"

$licensePublishArgs = @(
    'publish', $licenseProj,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained',
    '-o', $licenseOut,
    '-p:Platform=x64',
    '--nologo'
) + $dotnetVersionArgs
& dotnet @licensePublishArgs
if ($LASTEXITCODE -ne 0) { throw "LicenseTool publish failed" }
if (-not (Test-Path (Join-Path $licenseOut "Fortiva.LicenseTool.exe"))) {
    throw "Fortiva.LicenseTool.exe missing after publish"
}
Write-Host "  Done: $licenseOut" -ForegroundColor Green

# ── Browser extension (Chrome/Edge autofill) ────────────────────────────────
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Staging browser extension" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

function Copy-FilteredExtension {
    param([string]$SourceDir, [string]$DestDir)
    if (-not (Test-Path $SourceDir)) { throw "extension/ folder missing" }
    Remove-Item -Recurse -Force $DestDir -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $DestDir | Out-Null
    Get-ChildItem $SourceDir -File | ForEach-Object {
        $name = $_.Name
        if ($name -eq 'content.js') { return }
        if ($name -like 'com.fortiva.browserbridge*.json') { return }
        Copy-Item $_.FullName (Join-Path $DestDir $name) -Force
    }
    if (-not (Test-Path (Join-Path $DestDir 'manifest.json'))) {
        throw "extension manifest.json missing after filtered copy"
    }
}

$extSrc = Join-Path $root "extension"
$extOut = Join-Path $root "dist\extension"
Copy-FilteredExtension -SourceDir $extSrc -DestDir $extOut
Write-Host "  Done: $extOut" -ForegroundColor Green

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Bundling bridge + extension into apps" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$bridgeOut = Join-Path $root "dist\BrowserBridge"
foreach ($appOut in @("dist\Fortiva.Personal", "dist\Fortiva.Enterprise")) {
    $target = Join-Path $root $appOut
    if (-not (Test-Path $target)) { continue }
    $destBridge = Join-Path $target "BrowserBridge"
    $destExt = Join-Path $target "extension"
    Remove-Item -Recurse -Force $destBridge -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $destBridge | Out-Null
    Copy-Item (Join-Path $bridgeOut "*") $destBridge -Recurse -Force
    $hostPath = Join-Path $destBridge "Fortiva.BrowserBridge.Host.exe"
    if (Test-Path $hostPath) {
        $hash = (Get-FileHash $hostPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -Path (Join-Path $destBridge "bridge-host.sha256") -Value $hash -Encoding ascii -NoNewline
    }
    Copy-FilteredExtension -SourceDir $extOut -DestDir $destExt
    Write-Host "  Bundled into $appOut" -ForegroundColor Green
}

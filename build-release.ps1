#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Full release build for Fortiva (Personal, Enterprise, Admin).
    Uses VS MSBuild for XAML compilation, dotnet publish for self-contained output,
    then makepri.exe to generate resources.pri.
#>

Set-StrictMode -Version Latest
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
    $cached = Get-Item "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\makepri.exe" `
        -ErrorAction SilentlyContinue |
        Sort-Object FullName | Select-Object -Last 1
    if ($cached) { return $cached.FullName }
    return $null
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
    & $msbuild $proj /t:Build `
        /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
        /p:SelfContained=true /p:WindowsAppSDKSelfContained=true `
        /m /nologo /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw "MSBuild Build failed for $name" }

    # ── Step 3: Publish (copies EXE + DLLs + WinUI runtime) ─────────────────
    Write-Host "[3/4] Publish (dotnet)..."
    & dotnet publish $proj -c Release -r win-x64 --self-contained `
        -o $distDir -p:Platform=x64 --nologo --no-build 2>&1 |
        Where-Object { $_ -notmatch "^\s*$" } |
        ForEach-Object { Write-Host "  $_" }
    # --no-build skips re-building; just copies artifacts to PublishDir

    # If publish still fails (e.g. GenerateAppResourcesPri), fall through to
    # the manual PRI step below which is more reliable.
    $publishExitCode = $LASTEXITCODE

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

& dotnet publish $bridgeProj -c Release -r win-x64 --self-contained -o $bridgeOut `
    -p:Platform=x64 --nologo
if ($LASTEXITCODE -ne 0) { throw "BrowserBridge publish failed" }
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

& dotnet publish $licenseProj -c Release -r win-x64 --self-contained -o $licenseOut `
    -p:Platform=x64 --nologo
if ($LASTEXITCODE -ne 0) { throw "LicenseTool publish failed" }
if (-not (Test-Path (Join-Path $licenseOut "Fortiva.LicenseTool.exe"))) {
    throw "Fortiva.LicenseTool.exe missing after publish"
}
Write-Host "  Done: $licenseOut" -ForegroundColor Green

# ── Browser extension (Chrome/Edge autofill) ────────────────────────────────
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Staging browser extension" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$extSrc = Join-Path $root "extension"
$extOut = Join-Path $root "dist\extension"
if (-not (Test-Path $extSrc)) { throw "extension/ folder missing" }
Remove-Item -Recurse -Force $extOut -ErrorAction SilentlyContinue
Copy-Item $extSrc $extOut -Recurse -Force
if (-not (Test-Path (Join-Path $extOut "manifest.json"))) {
    throw "extension manifest.json missing after copy"
}
Write-Host "  Done: $extOut" -ForegroundColor Green

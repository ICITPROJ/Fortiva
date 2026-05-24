# Exhaustive QA: 30 boots, install/uninstall cycles, full test suite, static audits.
param(
    [int]$SmokeIterations = 30,
    [int]$InstallCycles = 3,
    [switch]$SkipBuild,
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$failures = @()
$passes = 0

function Get-IsccArgs {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\ensure-extension-key.ps1') | Out-Null
    $extensionId = & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\compute-extension-id.ps1')
    if ($extensionId -match 'REPLACE|NOT_SET' -or $extensionId.Length -ne 32) {
        throw "Invalid extension ID: '$extensionId'"
    }
    return @("/DExtensionId=$extensionId")
}

function Pass($msg) { Write-Host "  PASS: $msg" -ForegroundColor Green; $script:passes++ }
function Fail($msg) { Write-Host "  FAIL: $msg" -ForegroundColor Red; $script:failures += $msg }

function Step($name, [scriptblock]$action) {
    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host $name -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Cyan
    try { & $action; Pass $name }
    catch { Fail "$name - $($_.Exception.Message)" }
}

$exe = Join-Path $root 'dist\Fortiva.Personal\Fortiva.Personal.exe'
$installer = Join-Path $root 'dist\installers\FortivaPersonal-1.0.0-Setup.exe'
. (Join-Path $PSScriptRoot 'FortivaPersonalPaths.ps1')

Step "Release build" {
    if ($SkipBuild) {
        if (-not (Test-Path $exe)) { throw "Missing $exe - run without -SkipBuild" }
        Pass "Skipped build (exe present)"
        return
    }
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'build-release.ps1')
    if ($LASTEXITCODE -ne 0) { throw "build-release exit $LASTEXITCODE" }
    if (-not (Test-Path $exe)) { throw "Fortiva.Personal.exe missing" }
}

Step "Personal installer compile" {
    if ($SkipBuild) {
        if (-not (Test-Path $installer)) { throw "Missing installer" }
        return
    }
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\fetch-installer-prerequisites.ps1')
    if ($LASTEXITCODE -ne 0) { throw "fetch-installer-prerequisites failed" }
    $iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) { throw "ISCC not installed" }
    & $iscc @(Get-IsccArgs) (Join-Path $root 'packaging\installer\FortivaPersonal.iss') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
    if (-not (Test-Path $installer)) { throw "Installer missing" }
}

Step "Enterprise + Admin installer compile" {
    if ($SkipBuild) { return }
    $iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) { throw "ISCC not installed" }
    foreach ($iss in @('FortivaEnterprise.iss', 'FortivaAdmin.iss')) {
        & $iscc @(Get-IsccArgs) (Join-Path $root "packaging\installer\$iss") | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "ISCC failed for $iss" }
    }
    $ent = Join-Path $root 'dist\installers\FortivaEnterprise-1.0.0-Setup.exe'
    $adm = Join-Path $root 'dist\installers\FortivaAdmin-1.0.0-Setup.exe'
    if (-not (Test-Path $ent)) { throw "Enterprise installer missing" }
    if (-not (Test-Path $adm)) { throw "Admin installer missing" }
}

Step "Installer prerequisites audit" {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\fetch-installer-prerequisites.ps1')
    if ($LASTEXITCODE -ne 0) { throw "fetch-installer-prerequisites failed" }
    foreach ($req in @('MicrosoftEdgeWebview2Setup.exe', 'vc_redist.x64.exe')) {
        $p = Join-Path $root "packaging\prerequisites\$req"
        if (-not (Test-Path $p)) { throw "Missing prerequisite: $req" }
        if ((Get-Item $p).Length -lt 1MB) { throw "Prerequisite too small: $req" }
    }
    Pass "WebView2 + VC++ redistributables present"
}

Step "Static asset audit" {
    $assets = @(
        'src\Fortiva.AppHost\Assets\fortiva-logo.png',
        'src\Fortiva.AppHost\Assets\fortiva-logo-paranoia.png',
        'src\Fortiva.AppHost\Assets\fortiva.ico',
        'src\Fortiva.AppHost\Assets\fortiva-paranoia.ico'
    )
    foreach ($a in $assets) {
        $p = Join-Path $root $a
        if (-not (Test-Path $p)) { throw "Missing asset: $a" }
        if ((Get-Item $p).Length -lt 100) { throw "Asset too small: $a" }
    }
    Pass "All brand assets present"
}

Step "Browser bridge publish audit" {
    $bridgeExe = Join-Path $root 'dist\BrowserBridge\Fortiva.BrowserBridge.Host.exe'
    if (-not (Test-Path $bridgeExe)) { throw "BrowserBridge host missing - rebuild required" }
}

Step "Browser extension staging audit" {
    $extDir = Join-Path $root 'dist\extension'
    $required = @('manifest.json', 'background.js', 'content.js', 'popup.html', 'popup.js')
    if (-not (Test-Path $extDir)) { throw "dist\extension missing - rebuild required" }
    foreach ($f in $required) {
        $p = Join-Path $extDir $f
        if (-not (Test-Path $p)) { throw "Extension file missing: $f" }
    }
    $bg = Get-Content (Join-Path $extDir 'background.js') -Raw
    if ($bg -notmatch 'browserbridge\.personal' -or $bg -notmatch 'browserbridge\.enterprise') {
        throw "background.js must reference edition-specific native hosts"
    }
}

Step "Enterprise + Admin publish audit" {
    $entExe = Join-Path $root 'dist\Fortiva.Enterprise\Fortiva.Enterprise.exe'
    $admExe = Join-Path $root 'dist\Fortiva.Admin\Fortiva.Admin.exe'
    $licExe = Join-Path $root 'dist\LicenseTool\Fortiva.LicenseTool.exe'
    if (-not (Test-Path $entExe)) { throw "Fortiva.Enterprise.exe missing" }
    if (-not (Test-Path $admExe)) { throw "Fortiva.Admin.exe missing" }
    if (-not (Test-Path $licExe)) { throw "Fortiva.LicenseTool.exe missing" }
}

Step "Published output asset audit" {
    $pubAssets = @('fortiva-logo.png', 'fortiva.ico', 'fortiva-logo-paranoia.png')
    foreach ($a in $pubAssets) {
        $p = Join-Path $root "dist\Fortiva.Personal\Assets\$a"
        if (-not (Test-Path $p)) { throw "Missing in publish: $a" }
    }
}

Step "Core unit tests (full suite)" {
    dotnet test (Join-Path $root 'tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj') --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }
}

Step "Script tests" {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\test-scripts.ps1')
    if ($LASTEXITCODE -ne 0) { throw "script tests failed" }
}

Step "Headless vault integration (full suite x1)" {
    dotnet test (Join-Path $root 'tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj') `
        --filter "FullyQualifiedName~VaultIntegrationTests|FullyQualifiedName~VaultSession" `
        --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "vault tests failed" }
}

Step "Repeat vault lock test x10" {
    for ($i = 1; $i -le 10; $i++) {
        dotnet test (Join-Path $root 'tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj') `
            --filter "FullyQualifiedName~VaultSession_Lock_DoesNotReenterHostLockHandler" `
            --verbosity quiet --no-build 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Lock re-entry test failed on iteration $i" }
    }
}

Step "Password generator stress tests" {
    dotnet test (Join-Path $root 'tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj') `
        --filter "FullyQualifiedName~Password" `
        --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "password tests failed" }
}

Step "FortivaPaths wipe contract" {
    dotnet test (Join-Path $root 'tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj') `
        --filter "FullyQualifiedName~FortivaPaths" `
        --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "path tests failed" }
}

Step "$SmokeIterations x smoke boot (15s each)" {
    $crashLog = Join-Path $env:LOCALAPPDATA 'FortivaPersonal\fortiva-crash.log'
    for ($i = 1; $i -le $SmokeIterations; $i++) {
        Stop-FortivaProcesses
        $beforeLog = if (Test-Path $crashLog) { (Get-Item $crashLog).Length } else { 0 }

        $proc = Start-FortivaSmokeProcess -FilePath $exe
        Start-Sleep -Seconds 8
        if ($proc.HasExited) {
            throw "Boot $i crashed immediately (exit $($proc.ExitCode))"
        }
        Start-Sleep -Seconds 7
        if ($proc.HasExited) {
            throw "Boot $i crashed during run (exit $($proc.ExitCode))"
        }

        $proc.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) }

        if (Test-Path $crashLog) {
            $afterLog = (Get-Item $crashLog).Length
            if ($afterLog -gt $beforeLog) {
                $newLines = Get-Content $crashLog | Select-Object -Last 5
                throw "Boot $i wrote crash log: $($newLines -join ' | ')"
            }
        }

        if ($i % 10 -eq 0) { Write-Host "    ... $i / $SmokeIterations boots OK" -ForegroundColor DarkGray }
    }
    Pass "$SmokeIterations consecutive boots without crash"
}

Step "5 x Enterprise smoke boot (12s each)" {
    $entExe = Join-Path $root 'dist\Fortiva.Enterprise\Fortiva.Enterprise.exe'
    $crashLog = Join-Path $env:LOCALAPPDATA 'FortivaEnterprise\fortiva-crash.log'
    for ($i = 1; $i -le 5; $i++) {
        Stop-FortivaProcesses
        $beforeLog = if (Test-Path $crashLog) { (Get-Item $crashLog).Length } else { 0 }
        $proc = Start-FortivaSmokeProcess -FilePath $entExe -ExtraEnvironment @{
            FORTIVA_SKIP_AUTO_UPDATE = '1'
            FORTIVA_ALLOW_DEV_LICENSE_KEY = '1'
        }
        Start-Sleep -Seconds 12
        if ($proc.HasExited) { throw "Enterprise boot $i crashed (exit $($proc.ExitCode))" }
        $proc.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) }
        if (Test-Path $crashLog) {
            $afterLog = (Get-Item $crashLog).Length
            if ($afterLog -gt $beforeLog) { throw "Enterprise boot $i wrote crash log" }
        }
    }
}

Step "5 x Admin smoke boot (12s each, elevated)" {
    $admExe = Join-Path $root 'dist\Fortiva.Admin\Fortiva.Admin.exe'
    $crashLog = Join-Path $env:LOCALAPPDATA 'FortivaAdmin\fortiva-crash.log'
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "  SKIP: not running elevated (Admin requires Administrator)" -ForegroundColor Yellow
        Pass "Admin smoke boot (skipped - not elevated)"
        return
    }
    for ($i = 1; $i -le 5; $i++) {
        Stop-FortivaProcesses
        $beforeLog = if (Test-Path $crashLog) { (Get-Item $crashLog).Length } else { 0 }
        $proc = Start-FortivaSmokeProcess -FilePath $admExe -ExtraEnvironment @{
            FORTIVA_ALLOW_DEV_LICENSE_KEY = '1'
        }
        Start-Sleep -Seconds 12
        if ($proc.HasExited) { throw "Admin boot $i crashed (exit $($proc.ExitCode))" }
        $proc.CloseMainWindow() | Out-Null
        Start-Sleep -Seconds 2
        if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) }
        if (Test-Path $crashLog) {
            $afterLog = (Get-Item $crashLog).Length
            if ($afterLog -gt $beforeLog) { throw "Admin boot $i wrote crash log" }
        }
    }
}

if (-not $SkipInstall) {
    Step "$InstallCycles x install -> run -> uninstall -> verify clean" {
        for ($cycle = 1; $cycle -le $InstallCycles; $cycle++) {
            Write-Host "  --- Cycle $cycle ---" -ForegroundColor Yellow
            Stop-FortivaProcesses
            & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\reset-fortiva-personal.ps1') -WipeOnly
            if ($LASTEXITCODE -ne 0) { throw "Pre-cycle wipe failed" }

            $proc = Start-Process -FilePath $installer `
                -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS' `
                -Wait -PassThru
            if ($proc.ExitCode -ne 0) { throw "Install cycle $cycle exit $($proc.ExitCode)" }

            $installedExe = Join-Path ${env:LOCALAPPDATA} 'Programs\icmclab studio\Fortiva Personal\Fortiva.Personal.exe'
            if (-not (Test-Path $installedExe)) { throw "Installed exe missing after cycle $cycle" }

            $extManifest = Join-Path ${env:LOCALAPPDATA} 'Programs\icmclab studio\Fortiva Personal\extension\manifest.json'
            $bridgeHost = Join-Path ${env:LOCALAPPDATA} 'Programs\icmclab studio\Fortiva Personal\BrowserBridge\Fortiva.BrowserBridge.Host.exe'
            if (-not (Test-Path $extManifest)) { throw "Extension not bundled in Personal installer (cycle $cycle)" }
            if (-not (Test-Path $bridgeHost)) { throw "BrowserBridge not bundled in Personal installer (cycle $cycle)" }

            $run = Start-FortivaSmokeProcess -FilePath $installedExe
            Start-Sleep -Seconds 12
            if ($run.HasExited) { throw "Installed app crashed on cycle $cycle" }
            $run.CloseMainWindow() | Out-Null
            Start-Sleep -Seconds 2
            if (-not $run.HasExited) { $run.Kill() }

            if (-not (Invoke-FortivaPersonalUninstaller)) {
                throw "Uninstall failed cycle $cycle"
            }
            Start-Sleep -Seconds 3
            Stop-FortivaProcesses
            Remove-FortivaPersonalUserData

            if (Test-FortivaPersonalVaultExists) { throw "Vault survived uninstall cycle $cycle" }
            foreach ($p in Get-FortivaPersonalDataPaths) {
                if (Test-Path -LiteralPath $p) { throw "Data path remains after cycle ${cycle}: $p" }
            }
            Pass "Install cycle $cycle complete"
        }
    }
}

Step "Vault integration repeat x10" {
    for ($i = 1; $i -le 10; $i++) {
        dotnet test (Join-Path $root 'tests\Fortiva.Core.Tests\Fortiva.Core.Tests.csproj') `
            --filter "FullyQualifiedName~VaultIntegrationTests" `
            --verbosity quiet 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Vault integration failed iteration $i" }
        if ($i % 5 -eq 0) { Write-Host "    ... vault iteration $i/10" -ForegroundColor DarkGray }
    }
}

Write-Host ""
Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host "STRESS AUDIT SUMMARY" -ForegroundColor Cyan
Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host "Passed steps: $passes"
if ($failures.Count -gt 0) {
    Write-Host "Failures ($($failures.Count)):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "ALL STRESS AUDIT CHECKS PASSED." -ForegroundColor Green
exit 0

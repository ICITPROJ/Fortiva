# Full QA matrix: unit tests, script tests, builds, installers, smoke launch.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$failures = 0
function Step($name, [scriptblock]$action) {
    Write-Host ""
    Write-Host "========== $name ==========" -ForegroundColor Cyan
    try {
        & $action
        Write-Host "PASS: $name" -ForegroundColor Green
    }
    catch {
        Write-Host "FAIL: $name - $($_.Exception.Message)" -ForegroundColor Red
        $script:failures++
    }
}

Step "Core unit tests (63+)" {
    dotnet test tests/Fortiva.Core.Tests/Fortiva.Core.Tests.csproj --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test exit $LASTEXITCODE" }
}

Step "Script tests" {
    powershell -ExecutionPolicy Bypass -File "$root\scripts\test-scripts.ps1"
    if ($LASTEXITCODE -ne 0) { throw "script tests exit $LASTEXITCODE" }
}

Step "Release build" {
    powershell -ExecutionPolicy Bypass -File "$root\build-release.ps1"
    if ($LASTEXITCODE -ne 0) { throw "build-release exit $LASTEXITCODE" }
    if (-not (Test-Path "$root\dist\Fortiva.Personal\Fortiva.Personal.exe")) {
        throw "Fortiva.Personal.exe missing"
    }
}

Step "Installer compile (Personal)" {
    $iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) { throw "ISCC not installed" }
    & $iscc "$root\packaging\installer\FortivaPersonal.iss" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ISCC exit $LASTEXITCODE" }
    if (-not (Test-Path "$root\dist\installers\FortivaPersonal-1.0.0-Setup.exe")) {
        throw "Personal installer missing"
    }
}

Step "Smoke launch (15s)" {
    powershell -ExecutionPolicy Bypass -File "$root\smoke-test.ps1"
    if ($LASTEXITCODE -ne 0) { throw "smoke test exit $LASTEXITCODE" }
}

Step "Vault workflow (headless core)" {
    dotnet test tests/Fortiva.Core.Tests/Fortiva.Core.Tests.csproj `
        --filter "FullyQualifiedName~VaultIntegrationTests|FullyQualifiedName~VaultSession_Lock" `
        --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "vault workflow tests failed" }
}

Write-Host ""
if ($failures -gt 0) {
    Write-Host "$failures step(s) FAILED." -ForegroundColor Red
    exit 1
}
Write-Host "Full QA matrix PASSED." -ForegroundColor Green
exit 0

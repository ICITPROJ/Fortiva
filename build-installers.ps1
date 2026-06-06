param(

    [string]$Version = ""

)



$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot



function Resolve-IsccPath {

    $candidates = @(

        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),

        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),

        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')

    )

    foreach ($candidate in $candidates) {

        if (Test-Path $candidate) { return $candidate }

    }

    return $null

}



$iscc = Resolve-IsccPath

if (-not $iscc) {

    Write-Error "ISCC.exe not found. Install Inno Setup 6 (local or: choco install innosetup)."

    exit 1

}

Write-Host "Using ISCC: $iscc"

Write-Host "Fetching installer prerequisites (WebView2, VC++ redist)..."
& powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\fetch-installer-prerequisites.ps1')
if ($LASTEXITCODE -ne 0) { throw "fetch-installer-prerequisites failed" }

$prereqDir = Join-Path $root 'packaging\prerequisites'
foreach ($req in @('MicrosoftEdgeWebview2Setup.exe', 'vc_redist.x64.exe')) {
    $p = Join-Path $prereqDir $req
    if (-not (Test-Path $p)) { throw "Missing prerequisite: $p" }
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

& powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\ensure-extension-key.ps1') | Out-Null
$extensionId = & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\compute-extension-id.ps1')
if ($extensionId -match 'REPLACE|NOT_SET' -or $extensionId.Length -ne 32) {
    Write-Error "Invalid extension ID computed from manifest.json: '$extensionId'"
    exit 1
}
Write-Host "Browser extension ID: $extensionId" -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\write-browser-bridge-manifests.ps1') -ExtensionId $extensionId | Out-Null



$outDir = Join-Path $root "dist\installers"

New-Item -ItemType Directory -Force $outDir | Out-Null



$scripts = @(

    @{ Name = 'FortivaPersonal';   Required = $true  },

    @{ Name = 'FortivaEnterprise'; Required = $false },

    @{ Name = 'FortivaAdmin';      Required = $false }

)



$anyRequiredFail = $false



foreach ($entry in $scripts) {

    $s = $entry.Name

    $iss = Join-Path $root "packaging\installer\$s.iss"

    Write-Host "Building $s installer..."



    $isccArgs = @()

    if ($Version) { $isccArgs += "/DAppVersion=$Version" }

    if ($s -eq 'FortivaEnterprise') {
        $isccArgs += "/DExtensionId=$extensionId"
        $isccArgs += "/DExtensionUpdateUrl=https://github.com/ICITPROJ/Fortiva/releases/latest/download/fortiva-extension-updates.xml"
    }



    $log = Join-Path $env:TEMP "fortiva-$s-iscc.log"

    & $iscc @isccArgs $iss 2>&1 | Tee-Object -FilePath $log

    $exitCode = $LASTEXITCODE



    if ($exitCode -ne 0) {

        Write-Host "--- ISCC log: $log ---" -ForegroundColor Red

        Get-Content $log -Tail 40 | ForEach-Object { Write-Host $_ }

        if ($entry.Required) {

            Write-Error "$s FAILED (exit $exitCode)"

            $anyRequiredFail = $true

        } else {

            Write-Warning "$s FAILED (exit $exitCode) - optional edition skipped"

        }

    } else {

        Write-Host "$s installer OK" -ForegroundColor Green

    }

}



if ($anyRequiredFail) {

    Write-Error "Required Personal installer failed."

    exit 1

}



Write-Host ""

Write-Host "Installers in dist\installers:"

Get-ChildItem $outDir -Filter "*.exe" -ErrorAction SilentlyContinue |

    Select-Object Name, @{ N = 'Size'; E = { '{0:F1} MB' -f ($_.Length / 1MB) } } |

    Format-Table -AutoSize



$personal = Get-ChildItem $outDir -Filter "FortivaPersonal-*-Setup.exe" -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $personal) {

    Write-Error "FortivaPersonal-*-Setup.exe not found after build."

    exit 1

}



#Requires -Version 5.1

<#

.SYNOPSIS

    CI-safe validation of extension packaging (no browser, no deploy).

#>

Set-StrictMode -Version Latest

$ErrorActionPreference = "Stop"



$repo = Split-Path $PSScriptRoot -Parent

$ext = Join-Path $repo "extension"

$props = Join-Path $repo "Directory.Build.props"



function Fail([string]$Message) {

    Write-Error $Message

    exit 1

}



if (-not (Test-Path $ext)) { Fail "extension folder missing" }



$required = @(

    "manifest.json",

    "background.js",

    "popup.js",

    "popup.html",

    "fill-coordinator.js"

)

foreach ($f in $required) {

    if (-not (Test-Path (Join-Path $ext $f))) { Fail "Missing extension file: $f" }

}



$manifest = Get-Content (Join-Path $ext "manifest.json") -Raw | ConvertFrom-Json

if ($manifest.manifest_version -ne 3) { Fail "manifest_version must be 3" }

if (-not $manifest.content_scripts) { Fail "content_scripts required" }

$hasCoordinator = $false

foreach ($cs in $manifest.content_scripts) {

    if ($cs.js -contains "fill-coordinator.js") { $hasCoordinator = $true }

}

if (-not $hasCoordinator) { Fail "fill-coordinator.js must be registered in content_scripts" }



$war = $null
if ($manifest.PSObject.Properties.Name -contains "web_accessible_resources") {
    $war = $manifest.web_accessible_resources
}

if ($war) {

    foreach ($entry in $war) {

        if ($entry.resources -contains "page-fill-main.js") {

            Fail "page-fill-main.js must not be web_accessible (fill runs in isolated world only)"

        }

    }

}



if (-not (Test-Path $props)) { Fail "Directory.Build.props missing" }

$versionLine = Select-String -Path $props -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1

if (-not $versionLine) { Fail "Could not read Version from Directory.Build.props" }

$buildVersion = $versionLine.Matches[0].Groups[1].Value

if ($manifest.version -ne $buildVersion) {

    Fail "extension/manifest.json version $($manifest.version) != Directory.Build.props $buildVersion"

}



Write-Host "Extension manifest OK (v$buildVersion)" -ForegroundColor Green

exit 0


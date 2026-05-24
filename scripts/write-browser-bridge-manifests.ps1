# Writes edition-specific native messaging manifests with the computed extension ID.

param(
    [string]$ExtensionId,
    [string]$BridgeHostPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\extension')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ExtensionId)) {
    $ExtensionId = & (Join-Path $PSScriptRoot 'compute-extension-id.ps1')
}

if ($ExtensionId -match 'REPLACE|placeholder' -or $ExtensionId.Length -ne 32) {
    throw "Invalid extension ID: '$ExtensionId'"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Write-BridgeManifest {
    param([string]$Name, [string]$HostPath)
    $json = @{
        name            = $Name
        description     = 'Fortiva local credential bridge'
        path            = $HostPath
        type            = 'stdio'
        allowed_origins = @("chrome-extension://$ExtensionId/")
    } | ConvertTo-Json -Depth 4

    $dest = Join-Path $OutputDirectory ($Name + '.json')
    Set-Content -LiteralPath $dest -Value $json -Encoding UTF8
    Write-Host "Wrote $dest (extension ID: $ExtensionId)"
}

if ($BridgeHostPath) {
    Write-BridgeManifest 'com.fortiva.browserbridge.personal' $BridgeHostPath
    Write-BridgeManifest 'com.fortiva.browserbridge.enterprise' $BridgeHostPath
}
else {
    Write-BridgeManifest 'com.fortiva.browserbridge.personal' 'Fortiva.BrowserBridge.Host.exe'
    Write-BridgeManifest 'com.fortiva.browserbridge.enterprise' 'Fortiva.BrowserBridge.Host.exe'
}

return $ExtensionId

# Developer helper: build bridge + open Fortiva Settings for one-click browser setup.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Write-Host "Building browser bridge..."
dotnet publish (Join-Path $root 'src\Fortiva.BrowserBridge.Host\Fortiva.BrowserBridge.Host.csproj') `
    -c Release -r win-x64 --self-contained false `
    -o (Join-Path $root 'dist\BrowserBridge') | Out-Null

Write-Host ""
Write-Host "Next steps (in Fortiva):"
Write-Host "  1. Run Fortiva Personal"
Write-Host "  2. Open Settings -> Browser extension"
Write-Host "  3. Click 'Set up browser connection'"
Write-Host "  4. Click 'Open Edge extensions' -> Developer mode -> Load unpacked"
Write-Host "  5. Click 'Open extension folder' and select that folder"
Write-Host ""
Write-Host "Extension staging path (after setup):"
Write-Host "  $env:LOCALAPPDATA\FortivaPersonal\extension"

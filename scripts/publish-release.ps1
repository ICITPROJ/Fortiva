# Push main + tag to trigger GitHub Release workflow (builds installers + update manifest).
param(
    [string]$Version = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $raw = $props.Project.PropertyGroup.Version
    if ($raw -is [System.Array]) { $raw = $raw[0] }
    $Version = [string]$raw
}
$Version = $Version.Trim()
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid version: '$Version'" }

$tag = "v$Version"
Write-Host ""
Write-Host "Fortiva release publish" -ForegroundColor Cyan
Write-Host "  Version : $Version"
Write-Host "  Tag     : $tag"
Write-Host ""

Write-Host "Checking git status..." -ForegroundColor Yellow
$status = git status --porcelain
if ($status) {
    Write-Host "Uncommitted changes:" -ForegroundColor Yellow
    $status | ForEach-Object { Write-Host "  $_" }
    throw "Commit or stash changes before publishing."
}

Write-Host "Pushing main to origin..." -ForegroundColor Yellow
git push origin main
if ($LASTEXITCODE -ne 0) { throw "git push origin main failed (exit $LASTEXITCODE)" }
Write-Host "  main pushed OK" -ForegroundColor Green

$tagExists = [bool](git tag -l $tag)

if ($tagExists) {
    Write-Host "Tag $tag exists locally - pushing tag..." -ForegroundColor Yellow
} else {
    Write-Host "Creating tag $tag..." -ForegroundColor Yellow
    git tag -a $tag -m "Release $Version"
    if ($LASTEXITCODE -ne 0) { throw "git tag failed" }
}

Write-Host "Pushing tag $tag (triggers Release workflow)..." -ForegroundColor Yellow
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "git push origin $tag failed (exit $LASTEXITCODE)" }
Write-Host "  tag pushed OK" -ForegroundColor Green

Write-Host ""
Write-Host "Done. GitHub Actions is building the release (~8-10 min)." -ForegroundColor Green
Write-Host "Watch: https://github.com/ICITPROJ/Fortiva/actions/workflows/release.yml"
Write-Host ""
Write-Host "When the workflow is green, test in Fortiva:" -ForegroundColor Cyan
Write-Host '  Settings - Check for updates'
Write-Host ""

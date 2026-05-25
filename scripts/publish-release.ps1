# Legacy helper — releases are now automatic on push to main.
# This script only pushes main; GitHub Actions creates the tag and release.
param(
    [switch]$ForceBump
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

Write-Host ""
Write-Host "Fortiva release publish" -ForegroundColor Cyan
Write-Host ""
Write-Host "Releases are AUTOMATIC when you push to main." -ForegroundColor Green
Write-Host "  1. Commit your changes" -ForegroundColor DarkGray
Write-Host "  2. git push origin main" -ForegroundColor DarkGray
Write-Host "  3. Wait ~8-10 min for GitHub Actions (Release workflow)" -ForegroundColor DarkGray
Write-Host "  4. Settings -> Check for updates in the app" -ForegroundColor DarkGray
Write-Host ""

$status = git status --porcelain
if ($status) {
    Write-Host "Uncommitted changes:" -ForegroundColor Yellow
    $status | ForEach-Object { Write-Host "  $_" }
    throw "Commit first: git add -A && git commit -m `"your message`""
}

# Optional: bump local version file before push (CI also auto-bumps from latest tag)
if ($ForceBump) {
    & (Join-Path $PSScriptRoot 'bump-version.ps1') -Patch | Out-Null
    git add Directory.Build.props extension/manifest.json
    git commit -m "Bump version for release."
    Write-Host "Version bumped locally." -ForegroundColor Green
}

Write-Host "Pushing main to origin (triggers auto-release if there are new commits)..." -ForegroundColor Yellow
git push origin main
if ($LASTEXITCODE -ne 0) { throw "git push origin main failed (exit $LASTEXITCODE)" }

Write-Host ""
Write-Host "Push complete." -ForegroundColor Green
Write-Host "Watch Release workflow: https://github.com/ICITPROJ/Fortiva/actions/workflows/release.yml"
Write-Host ""
Write-Host "If this commit is NEW since the last release tag, CI will auto-publish the next patch version."
Write-Host "When the workflow is green, open Fortiva -> Settings -> Check for updates."
Write-Host ""

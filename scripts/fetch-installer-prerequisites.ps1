# Download WebView2 + VC++ redistributables for Inno Setup installers.
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$prereqDir = Join-Path $root 'packaging\prerequisites'
New-Item -ItemType Directory -Force -Path $prereqDir | Out-Null

$downloads = @(
    @{
        Name = 'MicrosoftEdgeWebview2Setup.exe'
        Url  = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'
        MinBytes = 1MB
    },
    @{
        Name = 'vc_redist.x64.exe'
        Url  = 'https://aka.ms/vs/17/release/vc_redist.x64.exe'
        MinBytes = 5MB
    }
)

foreach ($item in $downloads) {
    $dest = Join-Path $prereqDir $item.Name
    if ((Test-Path $dest) -and -not $Force) {
        $len = (Get-Item $dest).Length
        if ($len -ge $item.MinBytes) {
            Write-Host "OK  $($item.Name) ($([math]::Round($len / 1MB, 1)) MB)" -ForegroundColor DarkGray
            continue
        }
        Write-Host "Re-downloading $($item.Name) (file too small or corrupt)" -ForegroundColor Yellow
    }

    Write-Host "Downloading $($item.Name)..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $item.Url -OutFile $dest -UseBasicParsing
    $len = (Get-Item $dest).Length
    if ($len -lt $item.MinBytes) {
        Remove-Item -LiteralPath $dest -Force
        throw "Download failed or truncated: $($item.Name) ($len bytes)"
    }
    Write-Host "Saved $($item.Name) ($([math]::Round($len / 1MB, 1)) MB)" -ForegroundColor Green
}

Write-Host "Prerequisites ready in packaging\prerequisites" -ForegroundColor Green

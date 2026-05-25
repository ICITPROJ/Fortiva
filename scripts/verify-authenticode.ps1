param(
    [Parameter(Mandatory = $true)]
    [string[]]$Paths,

    [string]$PublisherContains = 'icmclab'
)

$ErrorActionPreference = 'Stop'

if ($env:FORTIVA_SKIP_CODESIGN -eq '1') {
    Write-Warning 'FORTIVA_SKIP_CODESIGN=1 — skipping Authenticode verification (development only).'
    exit 0
}

$failures = @()

foreach ($path in $Paths) {
    if (-not (Test-Path $path)) {
        $failures += "Missing file: $path"
        continue
    }

    $sig = Get-AuthenticodeSignature -FilePath $path
    if ($sig.Status -ne 'Valid') {
        $failures += "$path — signature status: $($sig.Status)"
        continue
    }

    $subject = $sig.SignerCertificate.Subject
    if ($subject -notmatch [regex]::Escape($PublisherContains)) {
        $failures += "$path — unexpected publisher: $subject"
    }
}

if ($failures.Count -gt 0) {
    Write-Host 'Authenticode verification failed:' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'Configure GitHub secrets CODESIGN_PFX_BASE64 and CODESIGN_PFX_PASSWORD, or set FORTIVA_SKIP_CODESIGN=1 only on dev forks.' -ForegroundColor Yellow
    exit 1
}

Write-Host "Verified Authenticode on $($Paths.Count) file(s)." -ForegroundColor Green

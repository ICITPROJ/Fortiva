# Shared Enterprise/Admin data paths — keep in sync with:
#   src/Fortiva.Core/Platform/FortivaPaths.cs
#   packaging/installer/FortivaEnterprise.iss
#   packaging/installer/FortivaAdmin.iss

function Get-FortivaEnterpriseLocalPaths {
    @(
        Join-Path $env:LOCALAPPDATA 'FortivaEnterprise'
    )
}

function Get-FortivaAdminLocalPaths {
    @(
        Join-Path $env:LOCALAPPDATA 'FortivaAdmin'
    )
}

function Get-FortivaProgramDataPaths {
    @(
        Join-Path $env:ProgramData 'Fortiva'
        Join-Path $env:ProgramData 'Fortiva\audit'
    )
}

function Get-FortivaEnterpriseVaultPath {
    Join-Path $env:ProgramData 'Fortiva\vault.fva'
}

function Remove-FortivaEnterpriseLocalData {
    foreach ($p in Get-FortivaEnterpriseLocalPaths) {
        if (Test-Path -LiteralPath $p) {
            Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop
            Write-Host "   Deleted: $p"
        }
    }
}

function Remove-FortivaAdminLocalData {
    foreach ($p in Get-FortivaAdminLocalPaths) {
        if (Test-Path -LiteralPath $p) {
            Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop
            Write-Host "   Deleted: $p"
        }
    }
}

function Remove-FortivaProgramDataAudit {
    $audit = Join-Path $env:ProgramData 'Fortiva\audit'
    if (Test-Path -LiteralPath $audit) {
        Remove-Item -LiteralPath $audit -Recurse -Force -ErrorAction Stop
        Write-Host "   Deleted: $audit"
    }
}

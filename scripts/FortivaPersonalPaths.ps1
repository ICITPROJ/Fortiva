# Shared Personal-edition data paths — keep in sync with:
#   src/Fortiva.Core/Platform/FortivaPaths.cs
#   packaging/installer/FortivaPersonal.iss

function Get-FortivaPersonalDataPaths {
    @(
        Join-Path $env:APPDATA 'Fortiva\Personal'
        Join-Path $env:APPDATA 'Fortiva'
        Join-Path $env:LOCALAPPDATA 'FortivaPersonal'
        Join-Path $env:LOCALAPPDATA 'Fortiva'
    )
}

function Get-FortivaPersonalVaultPath {
    Join-Path $env:APPDATA 'Fortiva\vault.fva'
}

function Stop-FortivaProcesses {
    $names = @(
        'Fortiva.Personal', 'Fortiva.Enterprise', 'Fortiva.Admin',
        'Fortiva.BrowserBridge.Host'
    )
    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
        $running = $names | ForEach-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue }
        if (-not $running) { break }
        Start-Sleep -Milliseconds 400
    }
    Start-Sleep -Seconds 1
}

function Start-FortivaSmokeProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [hashtable]$ExtraEnvironment = @{ FORTIVA_SKIP_AUTO_UPDATE = '1' }
    )
    $saved = @{}
    foreach ($key in $ExtraEnvironment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, [string]$ExtraEnvironment[$key], 'Process')
    }
    try {
        return Start-Process -FilePath $FilePath -PassThru -ErrorAction Stop
    }
    finally {
        foreach ($key in $ExtraEnvironment.Keys) {
            if ($null -eq $saved[$key]) {
                [Environment]::SetEnvironmentVariable($key, $null, 'Process')
            }
            else {
                [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process')
            }
        }
    }
}

function Remove-FortivaPersonalUserData {
    foreach ($p in Get-FortivaPersonalDataPaths) {
        if (Test-Path -LiteralPath $p) {
            Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop
            Write-Host "   Deleted: $p"
        }
    }
}

function Test-FortivaPersonalVaultExists {
    Test-Path -LiteralPath (Get-FortivaPersonalVaultPath)
}

function Invoke-FortivaPersonalUninstaller {
    $entry = Get-ItemProperty `
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -like '*Fortiva Personal*' } |
        Select-Object -First 1

    if (-not $entry -or -not $entry.UninstallString) {
        return $false
    }

    $s = $entry.UninstallString.Trim()
    $exe = $null
    $args = $null

    if ($s -match '^"(?<exe>[^"]+)"\s*(?<args>.*)$') {
        $exe = $matches.exe
        $args = $matches.args.Trim()
    }
    elseif ($s -match '^(?<exe>[^\s]+\.exe)\s*(?<args>.*)$') {
        $exe = $matches.exe
        $args = $matches.args.Trim()
    }
    else {
        $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $s -Wait -PassThru -ErrorAction Stop
        return ($proc.ExitCode -eq 0)
    }

    if (-not (Test-Path -LiteralPath $exe)) {
        Write-Warning "Uninstaller not found: $exe"
        return $false
    }

    if ($args) {
        if ($args -notmatch '/SILENT|/VERYSILENT') { $args = "$args /SILENT" }
        $proc = Start-Process -FilePath $exe -ArgumentList $args -Wait -PassThru -ErrorAction Stop
    }
    else {
        $proc = Start-Process -FilePath $exe -ArgumentList '/SILENT' -Wait -PassThru -ErrorAction Stop
    }

    return ($proc.ExitCode -eq 0)
}

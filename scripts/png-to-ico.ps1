param(
    [Parameter(Mandatory)][string]$PngPath,
    [Parameter(Mandatory)][string]$IcoPath
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile($PngPath)
try {
    $size = 256
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()

    $hIcon = $bmp.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($hIcon)
        $fs = [System.IO.File]::Open($IcoPath, [System.IO.FileMode]::Create)
        try { $icon.Save($fs) }
        finally { $fs.Close() }
    }
    finally {
        [void][System.Drawing.Icon]::DestroyIcon($hIcon)
        $bmp.Dispose()
    }
}
finally { $src.Dispose() }

Write-Host "Wrote $IcoPath ($((Get-Item $IcoPath).Length) bytes)"

# Draws the Hanami sakura icon and writes a multi-size .ico (PNG-compressed entries).
# Run with Windows PowerShell 5.1 (uses System.Drawing from .NET Framework).
param([string]$Out = (Join-Path $PSScriptRoot "..\src\Hanami.App\Assets\hanami.ico"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-FlowerPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.Clear([System.Drawing.Color]::Transparent)
    $c = $size / 2.0
    $petalLen = $size * 0.46
    $petalWid = $size * 0.30
    for ($i = 0; $i -lt 5; $i++) {
        $state = $g.Save()
        $g.TranslateTransform($c, $c)
        $g.RotateTransform($i * 72 - 90)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        # petal: teardrop from centre outwards with a notch at the tip
        $path.AddBezier(0, 0, $petalWid * 0.9, $petalLen * 0.15, $petalWid * 0.75, $petalLen * 0.85, $petalWid * 0.18, $petalLen)
        $path.AddLine($petalWid * 0.18, $petalLen, 0, $petalLen * 0.88)
        $path.AddLine(0, $petalLen * 0.88, -$petalWid * 0.18, $petalLen)
        $path.AddBezier(-$petalWid * 0.18, $petalLen, -$petalWid * 0.75, $petalLen * 0.85, -$petalWid * 0.9, $petalLen * 0.15, 0, 0)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $petalLen),
            [System.Drawing.Color]::FromArgb(255, 255, 250, 252), [System.Drawing.Color]::FromArgb(255, 240, 128, 170))
        $g.FillPath($brush, $path)
        $g.Restore($state)
    }
    $r = $size * 0.09
    $centre = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 200, 60, 110))
    $g.FillEllipse($centre, $c - $r, $c - $r, 2 * $r, 2 * $r)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (New-FlowerPng $s) }
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
Write-Host "wrote $Out"

<#
.SYNOPSIS
  Generoi PC Cleanerin kuvakkeen: src\Cleaner.UI\Assets\PcCleaner.ico (16–256 px) ja PcCleaner.png (256 px).
  Piirretään System.Drawing-kirjastolla, joten kuvake on toistettavissa ilman kuvankäsittelyohjelmaa.

  Kuva: pyöristetty sininen neliö, jossa valkoinen luuta ja kolme kipinää.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\src\Cleaner.UI\Assets")
)
Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size
    $r = $s * 0.22                       # kulman säde
    $m = $s * 0.04                       # marginaali

    # Tausta: pyöristetty neliö, liukuväri sininen
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rect = New-Object System.Drawing.RectangleF $m, $m, ($s - 2 * $m), ($s - 2 * $m)
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255, 56, 132, 240)),
        ([System.Drawing.Color]::FromArgb(255, 22, 78, 170)), 60
    $g.FillPath($brush, $path)

    # Luudan varsi: paksu valkoinen viiva oikeasta yläkulmasta keskelle alas vasemmalle
    $white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    $pen = New-Object System.Drawing.Pen $white, ($s * 0.09)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, $s * 0.74, $s * 0.22, $s * 0.44, $s * 0.56)

    # Harjakset: viuhka (trapetsi) varren päässä
    $bristles = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($s * 0.47), ($s * 0.50)),
        (New-Object System.Drawing.PointF ($s * 0.58), ($s * 0.62)),
        (New-Object System.Drawing.PointF ($s * 0.40), ($s * 0.84)),
        (New-Object System.Drawing.PointF ($s * 0.18), ($s * 0.68))
    )
    $bristles.AddPolygon($pts)
    $g.FillPath((New-Object System.Drawing.SolidBrush $white), $bristles)

    # Harjasten raidat (vain isoissa koossa)
    if ($size -ge 32) {
        $linePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(140, 22, 78, 170)), ($s * 0.025)
        $g.DrawLine($linePen, $s * 0.30, $s * 0.64, $s * 0.41, $s * 0.75)
        $g.DrawLine($linePen, $s * 0.38, $s * 0.58, $s * 0.49, $s * 0.69)
    }

    # Kipinät
    $sparkBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 221, 87))
    foreach ($sp in @(@(0.22, 0.26, 0.07), @(0.34, 0.16, 0.045), @(0.80, 0.62, 0.055))) {
        $cx = $s * $sp[0]; $cy = $s * $sp[1]; $rad = $s * $sp[2]
        $star = New-Object System.Drawing.Drawing2D.GraphicsPath
        $star.AddPolygon([System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF $cx, ($cy - $rad)),
            (New-Object System.Drawing.PointF ($cx + $rad * 0.3), ($cy - $rad * 0.3)),
            (New-Object System.Drawing.PointF ($cx + $rad), $cy),
            (New-Object System.Drawing.PointF ($cx + $rad * 0.3), ($cy + $rad * 0.3)),
            (New-Object System.Drawing.PointF $cx, ($cy + $rad)),
            (New-Object System.Drawing.PointF ($cx - $rad * 0.3), ($cy + $rad * 0.3)),
            (New-Object System.Drawing.PointF ($cx - $rad), $cy),
            (New-Object System.Drawing.PointF ($cx - $rad * 0.3), ($cy - $rad * 0.3))
        ))
        $g.FillPath($sparkBrush, $star)
    }

    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    # Pilkku estää PowerShelliä purkamasta tavutaulukkoa yksittäisiksi tavuiksi.
    return ,$ms.ToArray()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bmp = Draw-Icon $size
    [byte[]]$png = Get-PngBytes $bmp
    $images += ,@($size, $png)
    if ($size -eq 256) { $bmp.Save((Join-Path $OutDir "PcCleaner.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO-tiedosto PNG-pakatuilla merkinnöillä (tuettu Windows Vistasta alkaen kaikissa koossa).
$icoPath = Join-Path $OutDir "PcCleaner.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type: icon
$bw.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $size = $img[0]; [byte[]]$bytes = $img[1]
    $bw.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))   # width (0 = 256)
    $bw.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))   # height
    $bw.Write([Byte]0)               # color count
    $bw.Write([Byte]0)               # reserved
    $bw.Write([UInt16]1)             # planes
    $bw.Write([UInt16]32)            # bit count
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($img in $images) { [byte[]]$data = $img[1]; $bw.Write($data, 0, $data.Length) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Output "Kirjoitettu $icoPath ($((Get-Item $icoPath).Length) tavua, $($images.Count) kokoa) ja PcCleaner.png"

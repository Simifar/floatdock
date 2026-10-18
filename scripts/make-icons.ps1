# Draws the FloatDock icon (a dark shelf with three tiles, the middle one magnified) and writes
#   assets/icon/FloatDock-<size>.png   16 .. 256
#   src/FloatDock.App/Assets/FloatDock.ico   (PNG-compressed icon, 16 .. 256)
#   assets/store/*.png                 the logo sizes of the Store package (see packaging/msix)
# Everything is drawn in code so the icon can be changed in one place; run it again after editing the colours.
param([string]$OutputRoot = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $path
}

# The icon on a 256 x 256 canvas, scaled to $size; $padding keeps room around it (Store tiles).
function Draw-Icon([System.Drawing.Graphics]$g, [float]$left, [float]$top, [float]$size) {
    $s = $size / 256.0
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    # Shelf.
    $shelf = New-RoundedPath ($left + 10 * $s) ($top + 74 * $s) (236 * $s) (116 * $s) (40 * $s)
    $shelfBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new($left, $top + 74 * $s)), ([System.Drawing.PointF]::new($left, $top + 190 * $s)), ([System.Drawing.Color]::FromArgb(255, 58, 64, 78)), ([System.Drawing.Color]::FromArgb(255, 32, 35, 42))
    $g.FillPath($shelfBrush, $shelf)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 255, 255, 255)), (([Math]::Max(1.0, 2.0 * $s)))
    $g.DrawPath($pen, $shelf)
    # Tiles standing on the shelf: small, large (magnified under the pointer), small.
    $tiles = @(
        @{ X = 18; Size = 48; From = @(255, 255, 169, 77); To = @(255, 244, 117, 52) },
        @{ X = 76; Size = 104; From = @(255, 99, 166, 255); To = @(255, 60, 110, 240) },
        @{ X = 190; Size = 48; From = @(255, 74, 222, 190); To = @(255, 28, 168, 150) })
    foreach ($tile in $tiles) {
        $size256 = $tile.Size; $x = $left + $tile.X * $s; $y = $top + (170 - $size256) * $s; $w = $size256 * $s
        $path = New-RoundedPath $x $y $w $w ($size256 * 0.24 * $s)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new($x, $y)), ([System.Drawing.PointF]::new($x, $y + $w)), ([System.Drawing.Color]::FromArgb($tile.From[0], $tile.From[1], $tile.From[2], $tile.From[3])), ([System.Drawing.Color]::FromArgb($tile.To[0], $tile.To[1], $tile.To[2], $tile.To[3]))
        $g.FillPath($brush, $path)
        $brush.Dispose(); $path.Dispose()
    }
    $shelfBrush.Dispose(); $pen.Dispose(); $shelf.Dispose()
}

function Save-Png([int]$width, [int]$height, [float]$iconSize, [string]$path) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Icon $g (($width - $iconSize) / 2) (($height - $iconSize) / 2) $iconSize
    $g.Dispose()
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

$root = [IO.Path]::GetFullPath($OutputRoot)
$sizes = 16, 24, 32, 48, 64, 128, 256
foreach ($size in $sizes) { Save-Png $size $size $size (Join-Path $root "assets/icon/FloatDock-$size.png") }

# ICO container with PNG images (supported since Windows Vista).
$stream = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$images = $sizes | ForEach-Object { , [IO.File]::ReadAllBytes((Join-Path $root "assets/icon/FloatDock-$_.png")) }
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$icoPath = Join-Path $root 'src/FloatDock.App/Assets/FloatDock.ico'
New-Item -ItemType Directory -Force (Split-Path $icoPath) | Out-Null
[IO.File]::WriteAllBytes($icoPath, $stream.ToArray())

# Store package logos: the icon with a little air around it on tiles of the sizes the manifest names.
$store = Join-Path $root 'assets/store'
Save-Png 44 44 40 (Join-Path $store 'Square44x44Logo.png')
Save-Png 50 50 46 (Join-Path $store 'StoreLogo.png')
Save-Png 71 71 62 (Join-Path $store 'Square71x71Logo.png')
Save-Png 150 150 128 (Join-Path $store 'Square150x150Logo.png')
Save-Png 310 150 128 (Join-Path $store 'Wide310x150Logo.png')
Save-Png 310 310 268 (Join-Path $store 'Square310x310Logo.png')
Save-Png 88 88 80 (Join-Path $store 'Square44x44Logo.targetsize-44_altform-unplated.png')
Write-Output "Icons written under $root"

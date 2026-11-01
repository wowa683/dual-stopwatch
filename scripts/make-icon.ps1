# Generates app.ico (256x256, PNG-compressed) for the stopwatch.
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$icoPath = Join-Path $root "app.ico"
$size = 256

$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

function New-RoundedPath([int]$x, [int]$y, [int]$w, [int]$h, [int]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$bgPath = New-RoundedPath 8 8 240 240 52
$bgBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 23, 27, 36))
$g.FillPath($bgBrush, $bgPath)
$borderPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 43, 51, 66)), 4
$g.DrawPath($borderPen, $bgPath)

$cyan = [System.Drawing.Color]::FromArgb(255, 63, 201, 255)
$cyanBrush = New-Object System.Drawing.SolidBrush $cyan

$crownPath = New-RoundedPath 112 42 32 20 6
$g.FillPath($cyanBrush, $crownPath)
$stemPath = New-RoundedPath 120 56 16 16 4
$g.FillPath($cyanBrush, $stemPath)

$ringPen = New-Object System.Drawing.Pen $cyan, 14
$g.DrawEllipse($ringPen, 54, 64, 148, 148)

$tickColor = [System.Drawing.Color]::FromArgb(255, 120, 132, 152)
$tickPen = New-Object System.Drawing.Pen $tickColor, 6
$g.DrawLine($tickPen, 128, 80, 128, 94)
$g.DrawLine($tickPen, 128, 182, 128, 196)
$g.DrawLine($tickPen, 68, 138, 82, 138)
$g.DrawLine($tickPen, 174, 138, 188, 138)

$handPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 178, 76)), 12
$handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($handPen, 128, 138, 128, 96)

$dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 242, 246, 253))
$g.FillEllipse($dotBrush, 118, 128, 20, 20)

$g.Dispose()

# Build a multi-size ICO with classic 32-bit DIB entries (best compatibility).
function Get-IconDib([System.Drawing.Bitmap]$source, [int]$size) {
    $scaled = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $scaledGraphics = [System.Drawing.Graphics]::FromImage($scaled)
    $scaledGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $scaledGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $scaledGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $scaledGraphics.Clear([System.Drawing.Color]::Transparent)
    $scaledGraphics.DrawImage($source, 0, 0, $size, $size)
    $scaledGraphics.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([UInt32]40)              # BITMAPINFOHEADER size
    $writer.Write([Int32]$size)            # width
    $writer.Write([Int32]($size * 2))      # height (xor + and)
    $writer.Write([UInt16]1)               # planes
    $writer.Write([UInt16]32)              # bpp
    $writer.Write([UInt32]0)               # compression
    $writer.Write([UInt32]($size * $size * 4))
    $writer.Write([Int32]0)
    $writer.Write([Int32]0)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]0)

    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $scaled.GetPixel($x, $y)
            $writer.Write($c.B)
            $writer.Write($c.G)
            $writer.Write($c.R)
            $writer.Write($c.A)
        }
    }

    $maskStride = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $maskRow = New-Object byte[] $maskStride
    for ($y = 0; $y -lt $size; $y++) {
        $writer.Write($maskRow)
    }

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    $scaled.Dispose()
    return $bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $images += ,(Get-IconDib $bmp $size)
}

$file = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([UInt16]0)                # reserved
$writer.Write([UInt16]1)                # type: icon
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $bytes = $images[$i]
    $dimension = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]0)              # palette
    $writer.Write([Byte]0)              # reserved
    $writer.Write([UInt16]1)            # planes
    $writer.Write([UInt16]32)           # bpp
    $writer.Write([UInt32]$bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $bytes.Length
}

foreach ($bytes in $images) {
    $writer.Write($bytes, 0, $bytes.Length)
}

$writer.Close()
$bmp.Dispose()

Write-Output ("Icon written: " + $icoPath)

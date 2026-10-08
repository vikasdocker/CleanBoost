# Regenerates the CleanBoost icon set.
#
#   powershell -ExecutionPolicy Bypass -File tools/generate-icons.ps1 -OutDir packaging/assets
#
# Produces the PNG set the MSIX manifest references, plus CleanBoost.ico.
#
# The .ico matters as much as the PNGs: Windows cannot use a PNG as an
# application icon, and ApplicationIcon in the csproj needs a real multi-frame
# .ico or the exe ships with no icon at all -- which is what left the Programs
# and Features entry showing a blank icon.

param(
    [Parameter(Mandatory=$true)][string]$OutDir
)

# A malformed icon would be silently embedded into the exe by the next build,
# so any error here must stop the script rather than emit a broken file.
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$accent = [System.Drawing.Color]::FromArgb(30, 111, 210)     # Windows blue
$accentDark = [System.Drawing.Color]::FromArgb(10, 44, 100)
$text = [System.Drawing.Color]::FromArgb(255, 255, 255)

<#
Draws one square icon at a single size. Returns the bitmap; the caller decides
whether to save it as PNG or pack it into an .ico.

The glyph is parameterised because "CB" is unreadable once shrunk to 16px, so the
small frames use a single "C" drawn at a larger scale instead of a downscaled
wordmark.
#>
function New-AppBitmap([int]$size, [string]$glyph, [double]$fontScale) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $margin = [int]($size * 0.10)
    $radius = [int]($size * 0.22)
    $rect = New-Object System.Drawing.RectangleF($margin, $margin, ($size - 2*$margin), ($size - 2*$margin))

    $path2 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [int](2 * $radius)
    $path2.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path2.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path2.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path2.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path2.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, $accent, $accentDark, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($brush, $path2)

    $fontSize = [single]($size * $fontScale)
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $sf.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip
    $textBrush = New-Object System.Drawing.SolidBrush($text)
    $g.DrawString($glyph, $font, $textBrush, $rect, $sf)

    $g.Dispose()
    $brush.Dispose()
    $path2.Dispose()
    $sf.Dispose()
    $textBrush.Dispose()
    $font.Dispose()
    return $bmp
}

# Encodes a bitmap as the DIB an .ico entry expects:
# BITMAPINFOHEADER + bottom-up BGRA XOR mask + 1bpp AND mask.
function Get-BmpDibBytes([System.Drawing.Bitmap]$Bmp) {
    $w = $Bmp.Width
    $h = $Bmp.Height

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $locked = $Bmp.LockBits($rect,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $locked.Stride
        $px = New-Object byte[] ($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $px, 0, $px.Length)
    }
    finally { $Bmp.UnlockBits($locked) }

    # AND mask: 1 bit per pixel, bottom-up, each row padded to a 4-byte boundary.
    $andStride = [int]([Math]::Floor(($w + 31) / 32) * 4)
    $and = New-Object byte[] ($andStride * $h)
    for ($row = 0; $row -lt $h; $row++) {
        $andRow = $row * $andStride
        $srcRow = $row * $stride
        for ($x = 0; $x -lt $w; $x++) {
            # Fully transparent pixels get a set bit; everything else stays opaque.
            # Floor, not cast: $x / 8 is float division in PowerShell and [int]
            # would round to nearest, walking the byte index off the end of the row.
            if ($px[$srcRow + ($x * 4) + 3] -eq 0) {
                $byteIndex = $andRow + [int][Math]::Floor($x / 8)
                $and[$byteIndex] = $and[$byteIndex] -bor (0x80 -shr ($x % 8))
            }
        }
    }

    $header = New-Object byte[] 40
    [Array]::Copy([BitConverter]::GetBytes([int]40),      0, $header,  0, 4)  # biSize
    [Array]::Copy([BitConverter]::GetBytes([int]$w),       0, $header,  4, 4)  # biWidth
    [Array]::Copy([BitConverter]::GetBytes([int]($h * 2)), 0, $header,  8, 4)  # biHeight = XOR + AND
    [Array]::Copy([BitConverter]::GetBytes([int16]1),      0, $header, 12, 2)  # biPlanes
    [Array]::Copy([BitConverter]::GetBytes([int16]32),     0, $header, 14, 2)  # biBitCount
    [Array]::Copy([BitConverter]::GetBytes([int]0),        0, $header, 16, 4)  # biCompression = BI_RGB
    [Array]::Copy([BitConverter]::GetBytes([int]0),        0, $header, 20, 4)  # biSizeImage
    [Array]::Copy([BitConverter]::GetBytes([int]0),        0, $header, 24, 4)  # biXPelsPerMeter
    [Array]::Copy([BitConverter]::GetBytes([int]0),        0, $header, 28, 4)  # biYPelsPerMeter
    [Array]::Copy([BitConverter]::GetBytes([int]0),        0, $header, 32, 4)  # biClrUsed
    [Array]::Copy([BitConverter]::GetBytes([int]0),        0, $header, 36, 4)  # biClrImportant

    $dib = New-Object byte[] (40 + ($stride * $h) + ($andStride * $h))
    [Array]::Copy($header, 0, $dib, 0, 40)
    [Array]::Copy($px,    0, $dib, 40, $stride * $h)
    [Array]::Copy($and,   0, $dib, 40 + ($stride * $h), $andStride * $h)
    return $dib
}

# Packs bitmaps into a multi-frame .ico (ICONDIR + ICONDIRENTRY[] + DIBs).
function New-IcoFile([string]$OutPath, [System.Drawing.Bitmap[]]$Bitmaps) {
    $images = @()
    foreach ($b in $Bitmaps) { $images += ,(Get-BmpDibBytes $b) }

    $count = $images.Count
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        $bw.Write([int16]0)         # idReserved
        $bw.Write([int16]1)         # idType = 1 (icon)
        $bw.Write([int16]$count)

        $offset = 6 + (16 * $count)
        for ($i = 0; $i -lt $count; $i++) {
            $w = $Bitmaps[$i].Width
            $h = $Bitmaps[$i].Height
            # 256 is stored as 0 in the one-byte width/height fields.
            $wb = if ($w -ge 256) { 0 } else { $w }
            $hb = if ($h -ge 256) { 0 } else { $h }
            $bw.Write([byte]$wb)          # bWidth
            $bw.Write([byte]$hb)          # bHeight
            $bw.Write([byte]0)             # bColorCount
            $bw.Write([byte]0)             # bReserved
            $bw.Write([int16]1)            # wPlanes
            $bw.Write([int16]32)           # wBitCount
            $bw.Write([int]$images[$i].Length)   # dwBytesInRes
            $bw.Write([int]$offset)              # dwImageOffset
            $offset += $images[$i].Length
        }
        foreach ($img in $images) { $bw.Write($img, 0, $img.Length) }
        $bw.Flush()
        [System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
    }
    finally {
        $bw.Dispose()
        $ms.Dispose()
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ── PNG set referenced by the MSIX manifest ────────────────────────────────
$pngs = @(
    @{ Size = 44;  Glyph = 'CB'; FontScale = 0.45; File = 'Square44x44Logo.png' },
    @{ Size = 150; Glyph = 'CB'; FontScale = 0.45; File = 'Square150x150Logo.png' },
    @{ Size = 300; Glyph = 'CB'; FontScale = 0.45; File = 'StoreLogo.png' },
    @{ Size = 300; Glyph = 'CB'; FontScale = 0.45; File = 'Square310x310Logo.png' }
)
foreach ($p in $pngs) {
    $bmp = New-AppBitmap $p.Size $p.Glyph $p.FontScale
    $bmp.Save((Join-Path $OutDir $p.File), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "wrote $($p.File)"
}

# Wide logo: 310x150 canvas, same artwork inset on a transparent field.
$bmpWide = New-Object System.Drawing.Bitmap(310, 150, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmpWide)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::Transparent)
$sq = 120
$margin = 15
$rect = New-Object System.Drawing.RectangleF($margin, (($bmpWide.Height - $sq) / 2 - 5), $sq, $sq)
$path2 = New-Object System.Drawing.Drawing2D.GraphicsPath
$d = [int](2 * ($sq * 0.22))
$path2.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
$path2.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
$path2.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
$path2.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
$path2.CloseFigure()
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect, $accent, $accentDark, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
$g.FillPath($brush, $path2)
$font = New-Object System.Drawing.Font("Segoe UI", 46.0, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$sf.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip
$textBrush = New-Object System.Drawing.SolidBrush($text)
$textRect = New-Object System.Drawing.RectangleF(($sq + 2*$margin), 0, ($bmpWide.Width - $sq - 2*$margin), $bmpWide.Height)
$g.DrawString("CleanBoost", $font, $textBrush, $textRect, $sf)
$g.Dispose()
$bmpWide.Save((Join-Path $OutDir 'Wide310x150Logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmpWide.Dispose()
Write-Host "wrote Wide310x150Logo.png"

# ── Application icon (.ico) ────────────────────────────────────────────────
# Every frame Windows may ask for, each drawn natively at its own size so the
# glyph stays crisp instead of being downscaled from the 256px master.
$frames = @(
    @{ Size = 16;  Glyph = 'C';  FontScale = 0.70 },
    @{ Size = 24;  Glyph = 'C';  FontScale = 0.66 },
    @{ Size = 32;  Glyph = 'CB'; FontScale = 0.48 },
    @{ Size = 48;  Glyph = 'CB'; FontScale = 0.45 },
    @{ Size = 64;  Glyph = 'CB'; FontScale = 0.45 },
    @{ Size = 128; Glyph = 'CB'; FontScale = 0.45 },
    @{ Size = 256; Glyph = 'CB'; FontScale = 0.45 }
)

$bitmaps = @()
foreach ($f in $frames) {
    $bitmaps += ,(New-AppBitmap $f.Size $f.Glyph $f.FontScale)
}
$icoPath = Join-Path $OutDir 'CleanBoost.ico'
New-IcoFile $icoPath $bitmaps
foreach ($b in $bitmaps) { $b.Dispose() }

$frameList = ($frames | ForEach-Object { "$($_.Size)px" }) -join ', '
Write-Host "wrote CleanBoost.ico ($frameList, $((Get-Item $icoPath).Length) bytes)"
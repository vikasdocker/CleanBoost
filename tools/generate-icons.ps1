param(
    [Parameter(Mandatory=$true)][string]$OutDir
)

Add-Type -AssemblyName System.Drawing

$accent = [System.Drawing.Color]::FromArgb(30, 111, 210)     # Windows blue
$accentDark = [System.Drawing.Color]::FromArgb(10, 44, 100)
$text = [System.Drawing.Color]::FromArgb(255, 255, 255)

function New-AppIcon([int]$size, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
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

    $fontSize = [single]($size * 0.45)
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $sf.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip
    $textBrush = New-Object System.Drawing.SolidBrush($text)
    $g.DrawString("CB", $font, $textBrush, $rect, $sf)

    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "wrote $path"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

New-AppIcon 44  (Join-Path $OutDir "Square44x44Logo.png")
New-AppIcon 150 (Join-Path $OutDir "Square150x150Logo.png")
New-AppIcon 300 (Join-Path $OutDir "StoreLogo.png")
New-AppIcon 300 (Join-Path $OutDir "Square310x310Logo.png")

# Wide logo: 310x150 canvas, same artwork inset on a transparent field.
$bmp = New-Object System.Drawing.Bitmap(310, 150)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$sq = 120
$margin = 15
$rect = New-Object System.Drawing.RectangleF($margin, (($bmp.Height - $sq) / 2 - 5), $sq, $sq)
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
$textRect = New-Object System.Drawing.RectangleF(($sq + 2*$margin), 0, ($bmp.Width - $sq - 2*$margin), $bmp.Height)
$g.DrawString("CleanBoost", $font, $textBrush, $textRect, $sf)
$g.Dispose()
$bmp.Save((Join-Path $OutDir "Wide310x150Logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "wrote Wide310x150Logo.png"
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\assets\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$tempDir = Join-Path $env:TEMP ('audioswitch-icon-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDir | Out-Null
$pngPaths = New-Object System.Collections.Generic.List[string]

$background = [System.Drawing.Color]::FromArgb(255, 0, 120, 215)
$fontName = 'Segoe MDL2 Assets'
$glyph = [string][char]0xE7F5

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap -ArgumentList $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $inset = [math]::Max(1.0, $size * 0.055)
    $radius = [math]::Max(1.5, $size * 0.18)
    $rect = New-Object System.Drawing.RectangleF -ArgumentList $inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.SolidBrush -ArgumentList $background
    $graphics.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()

    $fontSize = [math]::Max(9.0, $size * 0.62)
    $font = New-Object System.Drawing.Font -ArgumentList $fontName, $fontSize, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::White)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF -ArgumentList 0, ($size * 0.02), $size, $size
    $graphics.DrawString($glyph, $font, $white, $textRect, $format)
    $format.Dispose()
    $white.Dispose()
    $font.Dispose()
    $graphics.Dispose()

    $pngPath = Join-Path $tempDir ("icon-{0}.png" -f $size)
    $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $pngPaths.Add($pngPath)
}

# Assemble a multi-size .ico file with PNG compressed entries (supported by Windows Vista+).
$pngData = @()
foreach ($pngPath in $pngPaths) {
    $pngData += ,([System.IO.File]::ReadAllBytes($pngPath))
}

$memory = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter -ArgumentList $memory
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngData[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $pngData[$i].Length
}

foreach ($data in $pngData) {
    $writer.Write($data)
}

$writer.Flush()
$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}
[System.IO.File]::WriteAllBytes($OutputPath, $memory.ToArray())
$writer.Dispose()
$memory.Dispose()

# Build a preview sheet of the generated sizes.
$sheet = New-Object System.Drawing.Bitmap -ArgumentList 700, 300
$sheetGraphics = [System.Drawing.Graphics]::FromImage($sheet)
$sheetGraphics.Clear([System.Drawing.Color]::FromArgb(255, 240, 240, 240))
$sheetGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$x = 20
foreach ($pngPath in $pngPaths) {
    $image = [System.Drawing.Image]::FromFile($pngPath)
    $display = [math]::Min(128, [math]::Max(16, $image.Width))
    $sheetGraphics.DrawImage($image, $x, 20, $display, $display)
    $label = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', 10
    $sheetGraphics.DrawString(("{0}px" -f $image.Width), $label, [System.Drawing.Brushes]::DimGray, [float]$x, [float](150))
    $label.Dispose()
    $image.Dispose()
    $x += $display + 16
}
$sheetGraphics.Dispose()
$previewPath = Join-Path (Split-Path -Parent $OutputPath) 'app-icon-preview.png'
$sheet.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()

Remove-Item $tempDir -Recurse -Force
Write-Host "Icon written to $OutputPath"
Write-Host "Preview written to $previewPath"

# app.ico を生成する。デザインは TrayIcons.cs の「配信中」と同じ。
# 使い方: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'app.ico'
$red = [System.Drawing.Color]::FromArgb(230, 20, 20)

function New-IconPng([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    # 32px 基準の座標で描いて拡大する。
    $g.ScaleTransform($size / 32.0, $size / 32.0)
    $brush = New-Object System.Drawing.SolidBrush $red
    $pen = New-Object System.Drawing.Pen $red, 3

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $x = 2.5; $y = 3.5; $w = 27; $h = 19; $d = 6
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)
    $g.DrawPath($pen, $path)

    $g.FillRectangle($brush, 14, 23, 4, 4)
    $g.FillRectangle($brush, 9, 27, 14, 3)
    $g.FillEllipse([System.Drawing.Brushes]::White, 11, 8, 10, 10)
    $g.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

# PNG 圧縮のエントリを並べた ICO を書き出す（Windows Vista 以降で有効な形式）。
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @()
foreach ($s in $sizes) { $images += , (New-IconPng $s) }

$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $images[$i]
    $writer.Write([byte]($s % 256)); $writer.Write([byte]($s % 256))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$data.Length); $writer.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $writer.Write($data) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($out, $file.ToArray())
"wrote $out"

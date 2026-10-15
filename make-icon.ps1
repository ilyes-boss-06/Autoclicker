# Builds app.ico (16-256 px, PNG entries) and src\logo.png from assets\icon-source.jpg:
# crops the rounded tile out of the artwork and makes the corners transparent.
param(
    [string]$Source = (Join-Path $PSScriptRoot 'assets\icon-source.jpg'),
    [string]$Out = (Join-Path $PSScriptRoot 'app.ico'),
    [string]$Logo = (Join-Path $PSScriptRoot 'src\logo.png')
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile($Source)

# Tile position inside the source artwork, as fractions of its width/height
$cropX = [int]($src.Width * 0.2985); $cropY = [int]($src.Height * 0.1326)
$cropSize = [int]($src.Width * 0.405); $radiusRatio = 0.17

function New-TilePng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = $size * $radiusRatio * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r, $r, 180, 90)
    $path.AddArc($size - $r, 0, $r, $r, 270, 90)
    $path.AddArc($size - $r, $size - $r, $r, $r, 0, 90)
    $path.AddArc(0, $size - $r, $r, $r, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.TextureBrush $src, ([System.Drawing.Drawing2D.WrapMode]::Clamp),
        (New-Object System.Drawing.Rectangle $cropX, $cropY, $cropSize, $cropSize)
    $scale = $size / $cropSize
    $brush.ScaleTransform($scale, $scale)
    $g.FillPath($brush, $path)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @($sizes | ForEach-Object { ,(New-TilePng $_) })

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img) }
$bw.Close()

# Larger copy for the in-app header (shown at 42 px, so 2x-3x for high-DPI screens)
[System.IO.File]::WriteAllBytes($Logo, (New-TilePng 128))
$src.Dispose()
Write-Host "Icon written to $Out and $Logo"

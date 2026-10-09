<#
    Generates the DotAHK application icon: a dark, minimalist tile fusing a mechanical
    gear with terminal angle brackets (< >) in vibrant cyan.

    It renders the artwork at 16, 32, 64, 128 and 256 px and packs them into a single
    multi-resolution .ico (PNG-compressed entries, which Windows Vista+ supports).

    Usage:
        powershell -NoProfile -ExecutionPolicy Bypass -File tools\generate-app-icon.ps1
#>

[CmdletBinding()]
param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not populated while param() defaults are evaluated in Windows
# PowerShell 5.1, so resolve the default here instead.
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutputPath = Join-Path $scriptRoot '..\src\DotAHK\Assets\app.ico'
}

Add-Type -AssemblyName System.Drawing

# --- Palette -----------------------------------------------------------------
$colBackTop    = [System.Drawing.Color]::FromArgb(255, 11, 14, 20)   # near-black
$colBackBottom = [System.Drawing.Color]::FromArgb(255, 20, 26, 35)   # deep slate
$colGear       = [System.Drawing.Color]::FromArgb(255, 34, 211, 238) # cyan-400
$colAccent     = [System.Drawing.Color]::FromArgb(255, 103, 232, 249)# cyan-300 (bright)
$colHole       = [System.Drawing.Color]::FromArgb(255, 12, 16, 22)   # inner void
$colEdge       = [System.Drawing.Color]::FromArgb(120, 34, 211, 238)

function New-RoundedRectPath {
    param([System.Drawing.RectangleF]$Rect, [float]$Radius)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $Radius * 2
    $path.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $s = [float]$Size
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Dark rounded tile.
    $inset = [Math]::Max(0.5, $s * 0.03)
    $rect = New-Object System.Drawing.RectangleF($inset, $inset, ($s - 2 * $inset), ($s - 2 * $inset))
    $path = New-RoundedRectPath -Rect $rect -Radius ($s * 0.22)

    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $colBackTop, $colBackBottom, 90)
    $g.FillPath($grad, $path)

    $edgePen = New-Object System.Drawing.Pen($colEdge, [float]([Math]::Max(0.75, $s * 0.015)))
    $g.DrawPath($edgePen, $path)

    $cx = $s / 2.0
    $cy = $s / 2.0

    $gearBrush = New-Object System.Drawing.SolidBrush($colGear)
    $holeBrush = New-Object System.Drawing.SolidBrush($colHole)

    # Gear ring geometry.
    $outer = $s * 0.30
    $inner = $s * 0.205

    # Radial teeth.
    $teeth = 8
    $toothLen = $s * 0.105
    $toothWidth = $s * 0.088
    for ($i = 0; $i -lt $teeth; $i++) {
        $state = $g.Save()
        $g.TranslateTransform($cx, $cy)
        $g.RotateTransform([float]($i * (360.0 / $teeth)))
        $g.FillRectangle(
            $gearBrush,
            [float](-$toothWidth / 2),
            [float](-($outer + $toothLen * 0.72)),
            [float]$toothWidth,
            [float]($toothLen + $s * 0.05))
        $g.Restore($state)
    }

    # Ring body, then punch the centre void.
    $g.FillEllipse($gearBrush, [float]($cx - $outer), [float]($cy - $outer), [float](2 * $outer), [float](2 * $outer))
    $g.FillEllipse($holeBrush, [float]($cx - $inner), [float]($cy - $inner), [float](2 * $inner), [float](2 * $inner))

    # Terminal brackets < > inside the void. They are kept clearly separated so the
    # pair reads as angle brackets rather than a single diamond.
    $bracketPen = New-Object System.Drawing.Pen($colAccent, [float]([Math]::Max(1.0, $s * 0.062)))
    $bracketPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $bracketPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $bracketPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $half = $s * 0.130    # vertical half-height of each bracket
    $reach = $s * 0.108   # apex distance from the centre
    $depth = $s * 0.058   # how far the arms lean back from the apex

    $left = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([float]($cx - $reach + $depth), [float]($cy - $half))),
        (New-Object System.Drawing.PointF([float]($cx - $reach), [float]$cy)),
        (New-Object System.Drawing.PointF([float]($cx - $reach + $depth), [float]($cy + $half)))
    )
    $right = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([float]($cx + $reach - $depth), [float]($cy - $half))),
        (New-Object System.Drawing.PointF([float]($cx + $reach), [float]$cy)),
        (New-Object System.Drawing.PointF([float]($cx + $reach - $depth), [float]($cy + $half)))
    )
    $g.DrawLines($bracketPen, $left)
    $g.DrawLines($bracketPen, $right)

    $gearBrush.Dispose()
    $holeBrush.Dispose()
    $bracketPen.Dispose()
    $edgePen.Dispose()
    $grad.Dispose()
    $path.Dispose()
    $g.Dispose()

    return $bmp
}

# --- Render each size to PNG in memory ---------------------------------------
$sizes = @(16, 32, 64, 128, 256)
$entries = New-Object 'System.Collections.Generic.List[object]'

foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -Size $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $entries.Add([pscustomobject]@{ Size = $size; Data = $stream.ToArray() })
    $stream.Dispose()
    $bitmap.Dispose()
}

# --- Assemble the ICO (header + directory + PNG payloads) --------------------
$outputFull = [System.IO.Path]::GetFullPath($OutputPath)
$outputDir = [System.IO.Path]::GetDirectoryName($outputFull)
if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$stream = [System.IO.File]::Create($outputFull)
$writer = New-Object System.IO.BinaryWriter($stream)

$writer.Write([UInt16]0)                 # reserved
$writer.Write([UInt16]1)                 # type: icon
$writer.Write([UInt16]$entries.Count)    # image count

$offset = 6 + (16 * $entries.Count)      # header + directory size
foreach ($entry in $entries) {
    $dim = if ($entry.Size -ge 256) { 0 } else { $entry.Size }
    $writer.Write([Byte]$dim)            # width  (0 = 256)
    $writer.Write([Byte]$dim)            # height (0 = 256)
    $writer.Write([Byte]0)               # colours in palette
    $writer.Write([Byte]0)               # reserved
    $writer.Write([UInt16]1)             # colour planes
    $writer.Write([UInt16]32)            # bits per pixel
    $writer.Write([UInt32]$entry.Data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $entry.Data.Length
}

foreach ($entry in $entries) {
    $writer.Write($entry.Data)
}

$writer.Flush()
$writer.Dispose()
$stream.Dispose()

$info = Get-Item -LiteralPath $outputFull
Write-Host ("Created {0} ({1} bytes, {2} resolutions: {3})" -f `
        $info.FullName, $info.Length, $entries.Count, (($sizes) -join ', '))

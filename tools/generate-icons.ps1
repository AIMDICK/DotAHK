# Generates the MSIX/WinUI image assets from the master application icon
# (src/DotAHK/DotAHK.ico). The 256x256 frame of the .ico is used as the source
# and rendered, high-quality and aspect-preserving, into every scaled PNG the
# Package.appxmanifest references so the Start Menu, taskbar and system search
# all display the correct branding.
#
# Usage (from the repository root):
#   powershell -ExecutionPolicy Bypass -File tools\generate-icons.ps1

Add-Type -AssemblyName System.Drawing

$repoRoot   = Split-Path -Parent $PSScriptRoot
$sourceIco  = Join-Path $repoRoot 'src\DotAHK\DotAHK.ico'
$assetsDir  = Join-Path $repoRoot 'src\DotAHK\Assets'

if (-not (Test-Path $sourceIco)) {
    throw "Master icon not found: $sourceIco"
}
if (-not (Test-Path $assetsDir)) {
    throw "Assets folder not found: $assetsDir"
}

# Pull the largest available frame (256x256) out of the multi-resolution .ico.
$icon   = New-Object System.Drawing.Icon($sourceIco, 256, 256)
$source = $icon.ToBitmap()

function Save-ScaledPng {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$Width,
        [int]$Height,
        [string]$Path
    )

    $target = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($target)
    $graphics.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    # Fit the square source inside the (possibly non-square) target, centred.
    $scale = [Math]::Min($Width / $Source.Width, $Height / $Source.Height)
    $drawW = [int][Math]::Round($Source.Width * $scale)
    $drawH = [int][Math]::Round($Source.Height * $scale)
    $drawX = [int](($Width - $drawW) / 2)
    $drawY = [int](($Height - $drawH) / 2)

    $graphics.DrawImage($Source, $drawX, $drawY, $drawW, $drawH)
    $graphics.Dispose()

    $target.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose()

    Write-Host ("  {0}  ({1}x{2})" -f (Split-Path -Leaf $Path), $Width, $Height)
}

# Logical size * scale factor. The manifest references the logical names and
# Windows resolves the .scale-200 variants automatically.
$assets = @(
    # Raster title-bar icon (WinUI cannot decode .ico in Image/ImageIconSource,
    # so the master .ico is rendered to this PNG for the custom title bar).
    @{ Name = 'AppIcon.png';                                            W = 44;   H = 44   },
    @{ Name = 'Square44x44Logo.scale-200.png';                          W = 88;   H = 88   },
    @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png';     W = 24;   H = 24   },
    @{ Name = 'Square44x44Logo.targetsize-48_altform-lightunplated.png';W = 48;   H = 48   },
    @{ Name = 'Square150x150Logo.scale-200.png';                        W = 300;  H = 300  },
    @{ Name = 'StoreLogo.png';                                          W = 50;   H = 50   },
    @{ Name = 'Wide310x150Logo.scale-200.png';                          W = 620;  H = 300  },
    @{ Name = 'SplashScreen.scale-200.png';                             W = 1240; H = 600  },
    @{ Name = 'LockScreenLogo.scale-200.png';                           W = 48;   H = 48   }
)

Write-Host "Generating image assets from $sourceIco"
foreach ($asset in $assets) {
    Save-ScaledPng -Source $source -Width $asset.W -Height $asset.H -Path (Join-Path $assetsDir $asset.Name)
}
Write-Host 'Done.'

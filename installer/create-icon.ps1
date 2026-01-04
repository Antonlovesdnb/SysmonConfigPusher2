# Create an icon file for the MSI installer
# This creates a simple blue shield icon matching the app logo

Add-Type -AssemblyName System.Drawing

function Create-Icon {
    param([string]$OutputPath)

    # Create 256x256, 48x48, 32x32, and 16x16 images
    $sizes = @(256, 48, 32, 16)
    $images = @()

    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        # Scale factor
        $scale = $size / 24.0

        # Draw shield (blue fill)
        $shieldPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $points = @(
            [System.Drawing.PointF]::new(12 * $scale, 2 * $scale),   # Top
            [System.Drawing.PointF]::new(4 * $scale, 6 * $scale),    # Left top
            [System.Drawing.PointF]::new(4 * $scale, 12 * $scale),   # Left mid
            [System.Drawing.PointF]::new(12 * $scale, 19.5 * $scale),# Bottom
            [System.Drawing.PointF]::new(20 * $scale, 12 * $scale),  # Right mid
            [System.Drawing.PointF]::new(20 * $scale, 6 * $scale)    # Right top
        )
        $shieldPath.AddPolygon($points)

        $blueBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 59, 130, 246))
        $graphics.FillPath($blueBrush, $shieldPath)

        # Draw shield outline
        $darkBluePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 30, 64, 175), [Math]::Max(1, $scale * 0.5))
        $graphics.DrawPath($darkBluePen, $shieldPath)

        # Draw eye (white ellipse and circle)
        $whitePen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.5, $scale * 1.5))
        $eyeRect = [System.Drawing.RectangleF]::new((12 - 4) * $scale, (11 - 2.5) * $scale, 8 * $scale, 5 * $scale)
        $graphics.DrawEllipse($whitePen, $eyeRect)

        $whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $pupilSize = 2.4 * $scale
        $graphics.FillEllipse($whiteBrush, (12 - 1.2) * $scale, (11 - 1.2) * $scale, $pupilSize, $pupilSize)

        $graphics.Dispose()
        $images += $bitmap
    }

    # Create ICO file
    $iconStream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($iconStream)

    # ICO header
    $writer.Write([Int16]0)           # Reserved
    $writer.Write([Int16]1)           # Type: 1 = ICO
    $writer.Write([Int16]$sizes.Count) # Number of images

    # Calculate offsets
    $headerSize = 6 + ($sizes.Count * 16)
    $dataOffset = $headerSize
    $imageData = @()

    foreach ($i in 0..($sizes.Count - 1)) {
        $bitmap = $images[$i]
        $ms = New-Object System.IO.MemoryStream
        $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngData = $ms.ToArray()
        $ms.Dispose()

        $size = $sizes[$i]
        $sizeVal = if ($size -ge 256) { 0 } else { $size }

        # Directory entry
        $writer.Write([Byte]$sizeVal)      # Width
        $writer.Write([Byte]$sizeVal)      # Height
        $writer.Write([Byte]0)              # Color palette
        $writer.Write([Byte]0)              # Reserved
        $writer.Write([Int16]1)             # Color planes
        $writer.Write([Int16]32)            # Bits per pixel
        $writer.Write([Int32]$pngData.Length) # Size of image data
        $writer.Write([Int32]$dataOffset)   # Offset to image data

        $imageData += ,$pngData
        $dataOffset += $pngData.Length
    }

    # Write image data
    foreach ($data in $imageData) {
        $writer.Write($data)
    }

    # Save to file
    $iconBytes = $iconStream.ToArray()
    [System.IO.File]::WriteAllBytes($OutputPath, $iconBytes)

    $writer.Dispose()
    $iconStream.Dispose()
    foreach ($img in $images) { $img.Dispose() }

    Write-Host "Created icon: $OutputPath"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$iconPath = Join-Path $scriptDir "sysmonpusher.ico"
Create-Icon -OutputPath $iconPath

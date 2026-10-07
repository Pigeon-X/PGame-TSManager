[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputIcon,
    [Parameter(Mandatory = $true)][string]$OutputIcon,
    [double]$RadiusRatio = 0.18,
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

$inputPath = (Resolve-Path -LiteralPath $InputIcon).Path
$outputPath = [IO.Path]::GetFullPath($OutputIcon)
$outputDirectory = Split-Path -Parent $outputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

function New-RoundedPath([float]$Width, [float]$Height, [float]$Radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = [Math]::Min($Radius * 2, [Math]::Min($Width, $Height))
    $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $path.AddArc($Width - $diameter, 0, $diameter, $diameter, 270, 90)
    $path.AddArc($Width - $diameter, $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc(0, $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$source = [System.Drawing.Image]::FromFile($inputPath)
$encoded = [System.Collections.Generic.List[byte[]]]::new()

try {
    foreach ($size in ($Sizes | Sort-Object -Unique)) {
        if ($size -lt 16 -or $size -gt 256) { throw "Unsupported icon size: $size" }

        $bitmap = [System.Drawing.Bitmap]::new(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

                $radius = [Math]::Max(2.0, $size * $RadiusRatio)
                $clip = New-RoundedPath $size $size $radius
                try {
                    $graphics.SetClip($clip)
                    $graphics.DrawImage(
                        $source,
                        [System.Drawing.Rectangle]::new(0, 0, $size, $size))
                }
                finally {
                    $clip.Dispose()
                }
            }
            finally {
                $graphics.Dispose()
            }

            $stream = [IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $encoded.Add($stream.ToArray())
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

$file = [IO.File]::Create($outputPath)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$Sizes.Count)

    $offset = 6 + (16 * $Sizes.Count)
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $size = $Sizes[$i]
        $data = $encoded[$i]
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$data.Length)
        $writer.Write([UInt32]$offset)
        $offset += $data.Length
    }

    foreach ($data in $encoded) {
        $writer.Write($data)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Host "Rounded icon created: $outputPath" -ForegroundColor Green

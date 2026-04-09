param(
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\..\src\FloatingDeskAssistant\Assets'),
    [switch]$Force = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class IconNativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyIcon(IntPtr handle);
}
"@

function Save-PngBackedIcon {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [string]$IcoPath
    )

    $iconHandle = $Bitmap.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
        $stream = [System.IO.File]::Create($IcoPath)
        try {
            $icon.Save($stream)
        }
        finally {
            $stream.Dispose()
            $icon.Dispose()
        }
    }
    finally {
        [IconNativeMethods]::DestroyIcon($iconHandle) | Out-Null
    }
}

$resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (-not (Test-Path $resolvedOutputDir)) {
    New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
}

$pngPath = Join-Path $resolvedOutputDir 'willGo.png'
$icoPath = Join-Path $resolvedOutputDir 'willGo.ico'

if (-not $Force -and (Test-Path $pngPath) -and (Test-Path $icoPath)) {
    Write-Host "Reusing existing branding assets: $pngPath / $icoPath" -ForegroundColor Yellow
    return
}

$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(58, 0, 20, 60))
$graphics.FillEllipse($shadowBrush, 24, 32, 208, 208)

$headRect = New-Object System.Drawing.Rectangle(18, 18, 220, 220)
$headBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $headRect,
    [System.Drawing.Color]::FromArgb(255, 58, 187, 255),
    [System.Drawing.Color]::FromArgb(255, 6, 108, 233),
    90
)
$graphics.FillEllipse($headBrush, $headRect)
$headPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 16, 54, 150), 5)
$graphics.DrawEllipse($headPen, $headRect)

$faceRect = New-Object System.Drawing.Rectangle(42, 58, 172, 152)
$faceBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
$facePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 36, 36, 36), 3)
$graphics.FillEllipse($faceBrush, $faceRect)
$graphics.DrawEllipse($facePen, $faceRect)

$leftEyeRect = New-Object System.Drawing.Rectangle(80, 40, 42, 56)
$rightEyeRect = New-Object System.Drawing.Rectangle(122, 40, 42, 56)
$eyePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 36, 36, 36), 3)
$graphics.FillEllipse($faceBrush, $leftEyeRect)
$graphics.FillEllipse($faceBrush, $rightEyeRect)
$graphics.DrawEllipse($eyePen, $leftEyeRect)
$graphics.DrawEllipse($eyePen, $rightEyeRect)

$pupilBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 20, 20, 20))
$graphics.FillEllipse($pupilBrush, 106, 63, 12, 20)
$graphics.FillEllipse($pupilBrush, 126, 63, 12, 20)
$highlightBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
$graphics.FillEllipse($highlightBrush, 110, 68, 4, 6)
$graphics.FillEllipse($highlightBrush, 130, 68, 4, 6)

$noseBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 238, 48, 64))
$nosePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 150, 14, 30), 2)
$graphics.FillEllipse($noseBrush, 111, 90, 30, 30)
$graphics.DrawEllipse($nosePen, 111, 90, 30, 30)
$graphics.FillEllipse($highlightBrush, 118, 96, 8, 8)

$linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 36, 36, 36), 3)
$graphics.DrawLine($linePen, 126, 120, 126, 176)

$smileRect = New-Object System.Drawing.Rectangle(82, 122, 88, 74)
$graphics.DrawArc($linePen, $smileRect, 12, 156)

$mouthBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 180, 22, 45))
$graphics.FillPie($mouthBrush, 90, 132, 72, 58, 12, 156)
$tongueBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 122, 146))
$graphics.FillPie($tongueBrush, 106, 159, 40, 20, 0, 180)

$whiskerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 32, 32, 32), 3)
$graphics.DrawLine($whiskerPen, 44, 112, 96, 120)
$graphics.DrawLine($whiskerPen, 42, 134, 92, 136)
$graphics.DrawLine($whiskerPen, 48, 156, 98, 148)
$graphics.DrawLine($whiskerPen, 156, 120, 208, 112)
$graphics.DrawLine($whiskerPen, 160, 136, 210, 134)
$graphics.DrawLine($whiskerPen, 154, 148, 204, 156)

$collarBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 219, 41, 52))
$collarPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 140, 12, 24), 3)
$graphics.FillRectangle($collarBrush, 56, 176, 140, 18)
$graphics.DrawRectangle($collarPen, 56, 176, 140, 18)

$bellBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 214, 64))
$bellPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 171, 120, 0), 3)
$graphics.FillEllipse($bellBrush, 108, 184, 36, 36)
$graphics.DrawEllipse($bellPen, 108, 184, 36, 36)
$graphics.DrawArc($bellPen, 114, 192, 24, 10, 0, 180)
$graphics.DrawLine($bellPen, 126, 202, 126, 214)
$graphics.FillEllipse([System.Drawing.Brushes]::SaddleBrown, 121, 209, 10, 10)

$sparklePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 255, 255, 255), 3)
$graphics.DrawLine($sparklePen, 186, 48, 198, 48)
$graphics.DrawLine($sparklePen, 192, 42, 192, 54)
$graphics.DrawLine($sparklePen, 64, 40, 74, 40)
$graphics.DrawLine($sparklePen, 69, 35, 69, 45)

$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
Save-PngBackedIcon -Bitmap $bitmap -IcoPath $icoPath

$sparklePen.Dispose()
$bellPen.Dispose()
$bellBrush.Dispose()
$collarPen.Dispose()
$collarBrush.Dispose()
$whiskerPen.Dispose()
$tongueBrush.Dispose()
$mouthBrush.Dispose()
$linePen.Dispose()
$nosePen.Dispose()
$noseBrush.Dispose()
$highlightBrush.Dispose()
$pupilBrush.Dispose()
$eyePen.Dispose()
$facePen.Dispose()
$faceBrush.Dispose()
$headPen.Dispose()
$headBrush.Dispose()
$shadowBrush.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "Generated cute Doraemon-style icon: $pngPath" -ForegroundColor Green
Write-Host "Generated cute Doraemon-style icon: $icoPath" -ForegroundColor Green

param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\VeloPic.App\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Rectangle,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    $bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $tile = New-RoundedRectanglePath ([System.Drawing.RectangleF]::new(10, 10, 236, 236)) 48
        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new(26, 22),
            [System.Drawing.PointF]::new(228, 238),
            [System.Drawing.ColorTranslator]::FromHtml('#246FE5'),
            [System.Drawing.ColorTranslator]::FromHtml('#15A8E8'))
        try {
            $graphics.FillPath($gradient, $tile)
        }
        finally {
            $gradient.Dispose()
            $tile.Dispose()
        }

        $frame = New-RoundedRectanglePath ([System.Drawing.RectangleF]::new(53, 57, 150, 142)) 16
        $framePen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 15)
        $framePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        try {
            $graphics.DrawPath($framePen, $frame)
        }
        finally {
            $framePen.Dispose()
            $frame.Dispose()
        }

        $cyanBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#70E0FF'))
        $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
        try {
            $mountain = @(
                [System.Drawing.PointF]::new(66, 181),
                [System.Drawing.PointF]::new(111, 125),
                [System.Drawing.PointF]::new(139, 156),
                [System.Drawing.PointF]::new(158, 136),
                [System.Drawing.PointF]::new(192, 181)
            )
            $graphics.FillPolygon($cyanBrush, $mountain)
            $graphics.FillEllipse($whiteBrush, 151, 82, 28, 28)
        }
        finally {
            $cyanBrush.Dispose()
            $whiteBrush.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

function Convert-ToPngBytes {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$Size
    )

    $target = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($target)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($Source, 0, 0, $Size, $Size)
    }
    finally {
        $graphics.Dispose()
    }

    try {
        $stream = [System.IO.MemoryStream]::new()
        try {
            $target.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $target.Dispose()
    }
}

$source = New-IconBitmap
try {
    $pngPath = Join-Path $OutputDirectory 'AppIcon.png'
    $source.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $images = foreach ($size in $sizes) {
        [PSCustomObject]@{
            Size = $size
            Data = Convert-ToPngBytes $source $size
        }
    }

    $iconPath = Join-Path $OutputDirectory 'VeloPic.ico'
    $stream = [System.IO.File]::Create($iconPath)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$images.Count)

        $offset = 6 + 16 * $images.Count
        foreach ($image in $images) {
            $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Data.Length)
            $writer.Write([uint32]$offset)
            $offset += $image.Data.Length
        }

        foreach ($image in $images) {
            $writer.Write($image.Data)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "已生成应用图标：$OutputDirectory"

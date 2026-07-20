param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$Version = "1.0.0.0",
    [string]$Publisher = "CN=VeloPic",
    [string]$SignCertificatePath = "",
    [string]$SignCertificatePassword = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = (Resolve-Path -LiteralPath $PublishDir).Path
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$packageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("velopic-msix-" + [guid]::NewGuid().ToString("N"))
$vfsRoot = Join-Path $packageRoot "VFS\ProgramFilesX64\VeloPic"
$assetRoot = Join-Path $packageRoot "Assets"
$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)

try {
    New-Item -ItemType Directory -Path $vfsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $publishRoot "*") -Destination $vfsRoot -Recurse -Force

    $manifestTemplate = Join-Path $repoRoot "packaging\msix\AppxManifest.xml"
    $manifestPath = Join-Path $packageRoot "AppxManifest.xml"
    Copy-Item -LiteralPath $manifestTemplate -Destination $manifestPath -Force
    $manifest = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8)
    $manifest = $manifest.Replace("__VERSION__", $Version).Replace("__PUBLISHER__", $Publisher)
    [System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))

    Add-Type -AssemblyName System.Drawing

    function New-PackageLogo {
        param(
            [Parameter(Mandatory = $true)][string]$SourcePath,
            [Parameter(Mandatory = $true)][string]$DestinationPath,
            [Parameter(Mandatory = $true)][int]$Size
        )

        $source = [System.Drawing.Image]::FromFile($SourcePath)
        $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

            $scale = [Math]::Min($Size / $source.Width, $Size / $source.Height)
            $width = [int][Math]::Round($source.Width * $scale)
            $height = [int][Math]::Round($source.Height * $scale)
            $x = [int](($Size - $width) / 2)
            $y = [int](($Size - $height) / 2)
            $graphics.DrawImage($source, $x, $y, $width, $height)
            $bitmap.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
            $source.Dispose()
        }
    }

    $sourceLogo = Join-Path $repoRoot "src\VeloPic.App\Assets\AppIcon.png"
    New-PackageLogo -SourcePath $sourceLogo -DestinationPath (Join-Path $assetRoot "Square44x44Logo.png") -Size 44
    New-PackageLogo -SourcePath $sourceLogo -DestinationPath (Join-Path $assetRoot "Square150x150Logo.png") -Size 150
    New-PackageLogo -SourcePath $sourceLogo -DestinationPath (Join-Path $assetRoot "StoreLogo.png") -Size 50

    $sdkBin = Join-Path $programFilesX86 "Windows Kits\10\bin"
    $makeAppx = Get-ChildItem $sdkBin -Recurse -Filter makeappx.exe |
        Where-Object { $_.FullName -like "*\x64\makeappx.exe" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($null -eq $makeAppx) {
        throw "makeappx.exe was not found. Install the Windows 10/11 SDK."
    }

    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($outputFullPath)) -Force | Out-Null
    & $makeAppx.FullName pack /d $packageRoot /p $outputFullPath /overwrite
    if ($LASTEXITCODE -ne 0) {
        throw "makeappx.exe failed with exit code $LASTEXITCODE."
    }

    if ($SignCertificatePath) {
        $signtool = Get-ChildItem $sdkBin -Recurse -Filter signtool.exe |
            Where-Object { $_.FullName -like "*\x64\signtool.exe" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($null -eq $signtool) {
            throw "signtool.exe was not found. Install the Windows 10/11 SDK."
        }

        & $signtool.FullName sign /fd SHA256 /a /f $SignCertificatePath /p $SignCertificatePassword $outputFullPath
        if ($LASTEXITCODE -ne 0) {
            throw "signtool.exe failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    if (Test-Path -LiteralPath $packageRoot) {
        Remove-Item -LiteralPath $packageRoot -Recurse -Force
    }
}

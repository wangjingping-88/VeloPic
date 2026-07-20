param(
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$env:NUGET_PACKAGES = 'D:\Program Files\NuGet\packages'

$projectPath = Join-Path $repoRoot 'src\VeloPic.App\VeloPic.App.csproj'
$exePath = Join-Path $repoRoot 'src\VeloPic.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\VeloPic.App.exe'

& 'D:\Program Files\dotnet\dotnet.exe' build $projectPath -c Debug -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw "VeloPic build failed. Exit code: $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "构建完成后未找到启动文件：$exePath"
}

if ($BuildOnly) {
    Write-Host "VeloPic build-only check: PASS"
    Write-Host $exePath
    return
}

& $exePath

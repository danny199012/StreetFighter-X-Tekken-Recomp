# Builds RecompLauncher as a single self-contained exe.
#
#   .\build.ps1              -> publish\RecompLauncher.exe (release, single file)
#   .\build.ps1 -Debug       -> quick debug build under src\RecompLauncher\bin\Debug
#
# No NuGet packages are required; the app only uses the .NET SDK + WPF.

param(
    [switch]$Debug
)

$ErrorActionPreference = 'Stop'
$here = Split-Path $PSScriptRoot -Parent
$project = Join-Path $PSScriptRoot 'src\RecompLauncher\RecompLauncher.csproj'

if ($Debug) {
    dotnet build $project -c Debug
} else {
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
        -o (Join-Path $PSScriptRoot 'publish')
    Write-Host ''
    Write-Host '==> publish\RecompLauncher.exe' -ForegroundColor Green
}

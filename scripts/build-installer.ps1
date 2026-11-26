param(
    [string]$Version = "2.2.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$localDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } elseif (Get-Command dotnet -ErrorAction SilentlyContinue) { "dotnet" } else { throw "dotnet SDK not found" }
$proj = Join-Path $root "src\LuminaCore.UI\LuminaCore.UI.csproj"
$publishDir = Join-Path $root "dist\publish-win-x64"
$distDir = Join-Path $root "dist"

Write-Host "1. Publishing self-contained win-x64 build..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& $dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "2. Creating portable ZIP archive..." -ForegroundColor Cyan
$zipPath = Join-Path $distDir "LuminaCore-Portable-v$Version-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "   -> $zipPath" -ForegroundColor Green

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host "3. Compiling Inno Setup One-Click Installer..." -ForegroundColor Cyan
    & $iscc (Join-Path $root "installer\LuminaCore.iss")
    Write-Host "   -> $distDir\LuminaCore-Setup-v$Version.exe" -ForegroundColor Green
} else {
    Write-Host "3. Inno Setup 6 not installed (skipping .exe installer; portable ZIP is ready)." -ForegroundColor Yellow
    Write-Host "   Install via: winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
}

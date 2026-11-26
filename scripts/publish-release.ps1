param(
    [string]$OutputDir = "$PSScriptRoot\..\dist\LuminaCore-win-x64"
)

$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { "dotnet" } else { "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
$proj = Join-Path $PSScriptRoot "..\src\LuminaCore.UI\LuminaCore.UI.csproj"

Write-Host "Publishing LuminaCore (self-contained win-x64) to $OutputDir ..." -ForegroundColor Cyan
& $dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Done! Output ready at: $OutputDir" -ForegroundColor Green

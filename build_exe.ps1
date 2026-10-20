# Space Marine 2 Customizer - Build Single-File Executable
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\dist"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor DarkYellow
Write-Host " Building SM2Customizer (Standalone Single-File .exe)    " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor DarkYellow

# Resolve dotnet executable
$dotnetCmd = "dotnet"
if (Test-Path "$env:LOCALAPPDATA\dotnet\dotnet.exe") {
    $dotnetCmd = "$env:LOCALAPPDATA\dotnet\dotnet.exe"
}

Write-Host "Using .NET SDK: $dotnetCmd" -ForegroundColor Cyan
& $dotnetCmd --version

$projectPath = "$PSScriptRoot\src\SM2Customizer\SM2Customizer.csproj"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found at: $projectPath"
}

# Clean output dir
if (Test-Path $OutputDir) {
    Write-Host "Cleaning output directory: $OutputDir" -ForegroundColor Gray
    Remove-Item -Path $OutputDir -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "Publishing self-contained win-x64 single-file executable..." -ForegroundColor Green

& $dotnetCmd publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
}

# Ensure Web assets are in dist/Web
$webSource = "$PSScriptRoot\src\SM2Customizer\Web"
$webDest = Join-Path $OutputDir "Web"
if (Test-Path $webSource) {
    Write-Host "Copying Web 3D studio assets to $webDest..." -ForegroundColor Cyan
    if (-not (Test-Path $webDest)) {
        New-Item -ItemType Directory -Path $webDest -Force | Out-Null
    }
    Copy-Item -Path "$webSource\*" -Destination $webDest -Recurse -Force
    if (Test-Path (Join-Path $webDest "Web")) {
        Remove-Item -Path (Join-Path $webDest "Web") -Recurse -Force -ErrorAction SilentlyContinue
    }
}


$exePath = Join-Path $OutputDir "SM2Customizer.exe"
if (Test-Path $exePath) {
    $sizeMb = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor DarkGreen
    Write-Host " SUCCESS! Standalone executable ready:                    " -ForegroundColor Green
    Write-Host " $exePath ($sizeMb MB)                                    " -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor DarkGreen
} else {
    Write-Error "Output executable was not found at $exePath"
}

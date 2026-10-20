# ==============================================================================
# SM2Customizer Pre-Push Verification & Repository Health Suite
# Run before committing or pushing to GitHub:
# powershell -ExecutionPolicy Bypass -File .\check.ps1
# ==============================================================================

param(
    [switch]$SkipFormat,
    [switch]$WithGame
)

$ErrorActionPreference = "Stop"
$script:AllPassed = $true
$script:Results = @()

function Report-Check([string]$name, [bool]$passed, [string]$detail) {
    if ($passed) {
        Write-Host "  [PASS] $name" -ForegroundColor Green
        if ($detail) { Write-Host "         $detail" -ForegroundColor DarkGray }
    } else {
        Write-Host "  [FAIL] $name" -ForegroundColor Red
        if ($detail) { Write-Host "         $detail" -ForegroundColor Yellow }
        $script:AllPassed = $false
    }
    $script:Results += [PSCustomObject]@{ Check = $name; Status = if ($passed) { "PASS" } else { "FAIL" }; Detail = $detail }
}

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " SM2Customizer Pre-Push Verification Suite                            " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# ------------------------------------------------------------------------------
# 1. Locate .NET SDK
# ------------------------------------------------------------------------------
$dotnet = "dotnet"
if (Test-Path "$env:LOCALAPPDATA\dotnet\dotnet.exe") {
    $dotnet = "$env:LOCALAPPDATA\dotnet\dotnet.exe"
}

try {
    $dotnetVer = & $dotnet --version 2>&1
    Report-Check ".NET 8 SDK Detection" $true ".NET SDK version: $dotnetVer"
} catch {
    Report-Check ".NET 8 SDK Detection" $false "Could not locate .NET SDK CLI!"
    exit 1
}

# ------------------------------------------------------------------------------
# 2. Build Solution (Release configuration)
# ------------------------------------------------------------------------------
Write-Host "`n[1/4] Building Solution..." -ForegroundColor Yellow
$buildOutput = & $dotnet build SM2Customizer.sln -c Release --nologo 2>&1
$buildExit = $LASTEXITCODE
if ($buildExit -eq 0) {
    Report-Check "Solution Build (Release)" $true "Build succeeded without compilation errors."
} else {
    $errLines = ($buildOutput | Where-Object { $_ -match "error " }) -join "`n"
    Report-Check "Solution Build (Release)" $false "Build failed!`n$errLines"
}

# ------------------------------------------------------------------------------
# 3. Web Studio & 3D Assets Integrity
# ------------------------------------------------------------------------------
Write-Host "`n[2/4] Verifying 3D Studio & Web Assets..." -ForegroundColor Yellow
$viewerHtmlPath = "$PSScriptRoot\src\SM2Customizer\Web\viewer.html"
$webDir = "$PSScriptRoot\src\SM2Customizer\Web"

if (-not (Test-Path $viewerHtmlPath)) {
    Report-Check "Viewer HTML File" $false "Missing src/SM2Customizer/Web/viewer.html"
} else {
    $htmlContent = Get-Content $viewerHtmlPath -Raw
    $scriptMatches = [regex]::Matches($htmlContent, '<script\s+src="([^"]+)"></script>')
    $missingScripts = @()
    foreach ($m in $scriptMatches) {
        $src = $m.Groups[1].Value
        $assetPath = Join-Path $webDir $src
        if (-not (Test-Path $assetPath) -or (Get-Item $assetPath).Length -lt 100) {
            $missingScripts += $src
        }
    }

    if ($missingScripts.Count -eq 0) {
        Report-Check "Viewer JavaScript Dependencies" $true "All $($scriptMatches.Count) referenced scripts present in Web/ directory."
    } else {
        Report-Check "Viewer JavaScript Dependencies" $false "Missing or empty scripts: $($missingScripts -join ', ')"
    }
}

# ------------------------------------------------------------------------------
# 4. Service & Model Sanity Verification (VerifyCustomizer)
# ------------------------------------------------------------------------------
Write-Host "`n[3/4] Running Automated Sanity & Offline Suite..." -ForegroundColor Yellow
$testArgs = @("run", "--project", "tests/VerifyCustomizer/VerifyCustomizer.csproj", "-c", "Release", "--no-build")
if ($WithGame) {
    $testArgs += "--require-game"
}

$testOutput = & $dotnet @testArgs 2>&1
$testExit = $LASTEXITCODE

if ($testExit -eq 0) {
    Report-Check "Automated Sanity Test Harness" $true "Model, config, and services verified successfully."
} else {
    $snippet = ($testOutput | Select-Object -Last 10) -join "`n"
    Report-Check "Automated Sanity Test Harness" $false "Sanity checks failed with code $($testExit):`n$snippet"
}

# ------------------------------------------------------------------------------
# 5. Repository Hygiene & Cleanliness
# ------------------------------------------------------------------------------
Write-Host "`n[4/4] Auditing Repository Hygiene & Push Readiness..." -ForegroundColor Yellow

# Check essential files
$hasLicense = Test-Path "$PSScriptRoot\LICENSE"
$hasNotices = Test-Path "$PSScriptRoot\THIRD_PARTY_NOTICES.md"
$hasReadme  = Test-Path "$PSScriptRoot\README.md"
$hasGitignore = Test-Path "$PSScriptRoot\.gitignore"

Report-Check "Required Documentation (LICENSE, README, NOTICES)" ($hasLicense -and $hasNotices -and $hasReadme) "License & Notice files verified."
Report-Check ".gitignore Configuration" $hasGitignore ".gitignore file present."

# Check for oversized files (> 25MB)
$largeFiles = Get-ChildItem -Path $PSScriptRoot -Recurse -File | 
    Where-Object { $_.FullName -notmatch '\\(\.git|dist|bin|obj|\.vs)\\' -and $_.Length -gt 25MB }

if ($largeFiles.Count -eq 0) {
    Report-Check "File Size Threshold (< 25MB)" $true "No oversized binaries tracked in source folders."
} else {
    $badList = ($largeFiles | ForEach-Object { "$($_.Name) ($([math]::Round($_.Length/1MB, 1)) MB)" }) -join ", "
    Report-Check "File Size Threshold (< 25MB)" $false "Found files over 25MB: $badList"
}

# Check for loose extracted game meshes in tests root
$looseMeshes = Get-ChildItem -Path "$PSScriptRoot\tests" -File -Filter "*.obj" -ErrorAction SilentlyContinue
if ($looseMeshes.Count -eq 0) {
    Report-Check "Copyright / Clean Assets Check" $true "No un-ignored raw game meshes in tests root."
} else {
    Report-Check "Copyright / Clean Assets Check" $false "Found $($looseMeshes.Count) loose game .obj files in tests root!"
}

# ------------------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------------------
Write-Host "`n======================================================================" -ForegroundColor Cyan
Write-Host " Verification Summary Table                                           " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
$script:Results | Format-Table -AutoSize Check, Status, Detail

if ($script:AllPassed) {
    Write-Host "`n>>> ALL CHECKS PASSED! REPOSITORY IS READY FOR GITHUB PUSH! <<<`n" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n>>> ONE OR MORE CHECKS FAILED! Resolve issues before pushing. <<<`n" -ForegroundColor Red
    exit 1
}

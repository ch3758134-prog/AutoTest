# AutoTest full independent pure UI - Windows-only compile and launch
# This never connects to TEST, PROJECT, DEVICE or an ECU.
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$hostRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $hostRoot 'src\AutoTest.Desktop\AutoTest.Desktop.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Windows .NET 10 SDK not found. Install the repository-required .NET SDK with WPF targeting packs.'
}
Write-Host '[UI] Building the clean WPF/Prism presentation-only application.' -ForegroundColor Cyan
dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "UI build failed with exit code $LASTEXITCODE. Do not report UX PASS."
}
Write-Host '[UI] Build passed locally. Starting the independent UI-only application.' -ForegroundColor Cyan
dotnet run --no-build --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "UI process exited with code $LASTEXITCODE."
}

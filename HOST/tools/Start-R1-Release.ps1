# Requires Windows PowerShell 5.1+ or PowerShell 7 on Windows.
# Build and launch exactly the just-compiled AutoTest.Desktop binary.
# Never kill a station controller process or launch an old binary on failure.
param()

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $root 'HOST\src\AutoTest.Desktop\AutoTest.Desktop.csproj'
$exe = Join-Path $root 'HOST\src\AutoTest.Desktop\bin\Release\net10.0-windows\AutoTest.Desktop.exe'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Cannot find AutoTest.Desktop project: $project"
}

$branch = (& git -C $root branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or $branch -ne 'R1') {
    throw "This launcher only supports checked-out R1. Current branch: $branch"
}

$alreadyRunning = @(Get-CimInstance Win32_Process -Filter "Name='AutoTest.Desktop.exe'" -ErrorAction SilentlyContinue)
if ($alreadyRunning.Count -gt 0) {
    $processLines = $alreadyRunning | ForEach-Object { "$($_.ProcessId): $($_.ExecutablePath)" }
    Write-Host "AutoTest is still running; build is blocked to protect USB/CAN/fixture state." -ForegroundColor Yellow
    $processLines | ForEach-Object { Write-Host $_ }
    throw "Stop real test/CAN tasks, safely dispose the fixture and exit AutoTest normally. Do not force-kill a live station."
}

$head = (& git -C $root rev-parse --short=12 HEAD).Trim()
Write-Host "Building R1 source HEAD: $head"
Push-Location $root
try {
    & dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE. The previous AutoTest EXE will NOT be launched."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Build returned success but the expected EXE is missing: $exe"
}

$file = Get-Item -LiteralPath $exe
Write-Host "Starting only the Release build output:"
Write-Host "  Path: $($file.FullName)"
Write-Host "  File timestamp (UTC): $($file.LastWriteTimeUtc.ToString('O'))"
Write-Host "  Git source HEAD: $head"
$started = Start-Process -FilePath $file.FullName -WorkingDirectory $file.DirectoryName -PassThru
Write-Host "  PID: $($started.Id)"
Write-Host "Verify the '构建 <MVID>' mark beside AutoTest in the app title, and see its tooltip for the exact process path."

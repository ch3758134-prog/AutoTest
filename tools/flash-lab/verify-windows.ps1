param(
    [ValidateSet("Debug","Release")] [string]$Configuration = "Debug",
    [switch]$AllHostTests
)
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
Set-Location $root
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logs = Join-Path $root "artifacts/flash-lab-f1/$stamp"
New-Item -ItemType Directory -Path $logs -Force | Out-Null
Write-Host "G4-F1 Windows offline build and HOST regression; NO WinUSB/CAN/ECU operations."
function Run-Recorded {
    param([string]$Label,[string[]]$Arguments)
    $path = Join-Path $logs "$Label.log"
    Write-Host "== $Label ==" -ForegroundColor Cyan
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $path
    if($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Label; raw log $path" -ForegroundColor Red
        throw "G4-F1 offline validation failed"
    }
}
Run-Recorded "01-flashlab-build" @("build","src/AutoTest.FlashLab/AutoTest.FlashLab.csproj","-c",$Configuration,"--nologo")
Run-Recorded "02-flashlab-file-tests" @("test","tests/AutoTest.FlashLab.Tests/AutoTest.FlashLab.Tests.csproj","-c",$Configuration,"--nologo")
Run-Recorded "03-physical-fence-tests" @("test","tests/AutoTest.Devices.Tests/AutoTest.Devices.Tests.csproj","-c",$Configuration,"--nologo","--filter","FullyQualifiedName~ReadOnlyDidPhysicalFenceTests|FullyQualifiedName~PhysicalTestRunFenceTests|FullyQualifiedName~WinUsbConnectFailureRecoveryTests")
Run-Recorded "04-host-desktop-build" @("build","src/AutoTest.Desktop/AutoTest.Desktop.csproj","-c",$Configuration,"--nologo")
if($AllHostTests) {
    Run-Recorded "05-all-device-tests" @("test","tests/AutoTest.Devices.Tests/AutoTest.Devices.Tests.csproj","-c",$Configuration,"--nologo")
    Run-Recorded "06-host-desktop-tests" @("test","tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj","-c",$Configuration,"--nologo")
}
Write-Host "OFFLINE BUILD/TEST PHASE DONE. Real WinUSB exclusivity and ECU response remain UNVERIFIED."
Write-Host "Logs: $logs"

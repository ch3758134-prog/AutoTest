# R1 source-candidate gate. No automatic WinUSB connection or physical DUT operation.
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'R1 WPF verification must run on Windows.'
}
$hostRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sln = Join-Path $hostRoot 'AutoTest.sln'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logRoot = Join-Path $hostRoot 'validation-logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$summary = Join-Path $logRoot "r1-$stamp-summary.txt"
$head = if (Get-Command git -ErrorAction SilentlyContinue) {
    (& git -C (Split-Path $hostRoot -Parent) rev-parse HEAD 2>&1 | Out-String).Trim()
} else { 'GIT_UNAVAILABLE' }
"R1 candidate verification / $stamp / $Configuration / HEAD $head" |
    Set-Content -Path $summary -Encoding utf8
Write-Host "R1 HEAD: $head"
Write-Host "Windows verification logs: $logRoot"

function Invoke-RequiredDotnet([string]$name, [string[]]$arguments) {
    $log = Join-Path $logRoot "r1-$stamp-$name.log"
    Write-Host "=== $name ===" -ForegroundColor Cyan
    & dotnet @arguments 2>&1 | Tee-Object -FilePath $log
    $exitCode = $LASTEXITCODE
    "$name : EXIT=$exitCode / $log" | Add-Content -Path $summary -Encoding utf8
    if ($exitCode -ne 0) {
        throw "$name failed with exit code $exitCode. Inspect $log. No PASS may be issued."
    }
}
try {
    Invoke-RequiredDotnet 'restore' @('restore', $sln)
    Invoke-RequiredDotnet 'build' @('build', $sln, '-c', $Configuration, '--no-restore')
    Invoke-RequiredDotnet 'test' @('test', $sln, '-c', $Configuration, '--no-build', '--no-restore')
    'SOURCE_BUILD_AND_TESTS_PASS. WPF_START_NOT_VERIFIED. HARDWARE_NOT_VERIFIED.' |
        Add-Content -Path $summary -Encoding utf8
    Write-Host "Source gate passed. UI startup and real ECU remain separate gates." -ForegroundColor Green
}
catch {
    "SOURCE_GATE_FAIL: $_" | Add-Content -Path $summary -Encoding utf8
    Write-Error $_
    exit 1
}

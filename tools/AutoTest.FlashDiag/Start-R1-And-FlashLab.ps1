# Launch R1 product UI and standalone CanTool Flash Lab in separate Windows processes.
# This does NOT allow both processes to open the same WinUSB device at once.
# Hardware ownership remains exclusive until a shared cross-process broker is implemented.
[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $Root).Path
$repo = Join-Path $root 'AutoTest_R1_Lab'
$flash = Join-Path $root 'AutoTest_CanTool_Flash_Lab'
$gitRemote = 'https://github.com/ch3758134-prog/AutoTest.git'
function Ensure-Worktree([string]$path,[string]$branch) {
    if (Test-Path $path) {
        if (-not (Test-Path (Join-Path $path '.git'))) {
            throw "Path exists but is not a Git worktree: $path"
        }
        Write-Host "Using existing worktree: $path"
        return
    }
    git -C $root rev-parse --is-inside-work-tree *> $null
    if ($LASTEXITCODE -ne 0) { throw "Root must be an existing AutoTest Git worktree." }
    git -C $root fetch origin $branch
    if ($LASTEXITCODE -ne 0) { throw "Fetch failed: $branch" }
    git -C $root worktree add --detach $path "origin/$branch"
    if ($LASTEXITCODE -ne 0) { throw "Worktree creation failed: $path" }
}
# Root is the existing AutoTest checkout; siblings are placed alongside it.
$parent = Split-Path -Parent $root
$repo = Join-Path $parent 'AutoTest_R1_Lab'
$flash = Join-Path $parent 'AutoTest_CanTool_Flash_Lab'
Ensure-Worktree $repo 'feature/r1-html-product-ui-20261110'
Ensure-Worktree $flash 'feature/cantool-flash-lab-20261110'
$r1Project = Join-Path $repo 'src/AutoTest.Desktop/AutoTest.Desktop.csproj'
$flashProject = Join-Path $flash 'tools/AutoTest.FlashDiag/AutoTest.FlashDiag.csproj'
if (-not $SkipBuild) {
    dotnet build $r1Project -c Release
    if ($LASTEXITCODE -ne 0) { throw "R1 build failed; not launching." }
    dotnet build $flashProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "CanTool Flash Lab build failed; not launching." }
}
$r1Exe = Join-Path $repo 'src/AutoTest.Desktop/bin/Release/net10.0-windows/AutoTest.Desktop.exe'
$flashExe = Join-Path $flash 'tools/AutoTest.FlashDiag/bin/Release/net10.0-windows/AutoTest.FlashDiag.exe'
if (-not (Test-Path $r1Exe)) { throw "R1 exe missing: $r1Exe" }
if (-not (Test-Path $flashExe)) { throw "Flash Lab exe missing: $flashExe" }
Start-Process -FilePath $r1Exe -WorkingDirectory (Split-Path -Parent $r1Exe)
Start-Process -FilePath $flashExe -WorkingDirectory (Split-Path -Parent $flashExe)
Write-Host "Opened both windows. WinUSB connection remains EXCLUSIVE; do not connect both to the same controller."

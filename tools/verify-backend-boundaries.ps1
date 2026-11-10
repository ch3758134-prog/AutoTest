# Backend architecture boundary check. Run from repository root:
# pwsh -NoProfile -File tools/verify-backend-boundaries.ps1
# It intentionally checks compile-time ownership, not runtime feature readiness.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projects = @(
  'AutoTest.Core',
  'AutoTest.Contracts',
  'AutoTest.Devices',
  'AutoTest.Projects',
  'AutoTest.Testing',
  'AutoTest.Specialists',
  'AutoTest.Records',
  'AutoTest.Application'
)
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($name in $projects) {
  $relative = "src/$name/$name.csproj"
  $path = Join-Path $repositoryRoot $relative
  if (-not (Test-Path -LiteralPath $path)) {
    $failures.Add("MISSING_PROJECT: $relative")
    continue
  }
  [xml]$project = Get-Content -LiteralPath $path -Raw
  foreach ($node in @($project.SelectNodes('//PropertyGroup/UseWPF'))) {
    if ($node.InnerText.Trim().ToLowerInvariant() -eq 'true') {
      $failures.Add("BACKEND_OWNS_WPF: $relative")
    }
  }
  foreach ($reference in @($project.SelectNodes('//ProjectReference'))) {
    $include = $reference.GetAttribute('Include')
    if ($include -match '(?i)AutoTest\.(Desktop|DeviceLab|LabLinkGui)(/|\\|\.)') {
      $failures.Add("BACKEND_REFERENCES_UI: $relative -> $include")
    }
  }
  foreach ($package in @($project.SelectNodes('//PackageReference'))) {
    $id = $package.GetAttribute('Include')
    if ($id -match '^(?i:Prism\.|MahApps\.|MaterialDesignThemes\.|PresentationFramework)') {
      $failures.Add("BACKEND_REFERENCES_UI_PACKAGE: $relative -> $id")
    }
  }
  $sourceRoot = Join-Path $repositoryRoot "src/$name"
  foreach ($file in @(Get-ChildItem -Path $sourceRoot -Filter *.xaml -File -Recurse)) {
    $failures.Add("BACKEND_CONTAINS_XAML: $($file.FullName)")
  }
}
if ($failures.Count -gt 0) {
  $failures | ForEach-Object { Write-Error $_ }
  exit 1
}
Write-Host "BACKEND_UI_BOUNDARY_PASS: $($projects.Count) production backend projects have no WPF UI project/package references or XAML."
Write-Host "NOTE: This is an architecture boundary check, not a physical-device functional acceptance."

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
  foreach ($group in @($project.Project.PropertyGroup)) {
    if ($null -ne $group -and [string]$group.UseWPF -match '^(?i:true)$') {
      $failures.Add("BACKEND_OWNS_WPF: $relative")
    }
  }
  foreach ($group in @($project.Project.ItemGroup)) {
    if ($null -eq $group) { continue }
    foreach ($reference in @($group.ProjectReference)) {
      if ($null -eq $reference) { continue }
      $include = [string]$reference.Include
      if ($include -match '(?i)AutoTest\.(Desktop|DeviceLab|LabLinkGui)(/|\\|\.)') {
        $failures.Add("BACKEND_REFERENCES_UI: $relative -> $include")
      }
    }
    foreach ($package in @($group.PackageReference)) {
      if ($null -eq $package) { continue }
      $id = [string]$package.Include
      if ($id -match '^(?i:Prism\.|MahApps\.|MaterialDesignThemes\.|PresentationFramework)') {
        $failures.Add("BACKEND_REFERENCES_UI_PACKAGE: $relative -> $id")
      }
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

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('FirstUse_NoProject','FirstUse_EmptyProject','Edit_NormalUse','Edit_EmptyCase')]
    [string] $Scenario,
    [string] $OutputDirectory = '.\G4_UI_Windows_Evidence'
)

# This script captures the ACTUAL foreground G4 WPF process on Windows.
# It does not create data, start a test, simulate an ECU, or fabricate screenshots.
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Real G4 UI screenshot capture requires Windows and an interactive desktop.'
}
Add-Type -AssemblyName System.Drawing
if (-not ("G4WindowRect" -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class G4WindowRect {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
}
'@ -Language CSharp
}

$process = Get-Process | Where-Object {
    ($_.ProcessName -eq 'AutoTest.G4Workbench' -or $_.ProcessName -eq 'AutoTest.Desktop') -and
    $_.MainWindowHandle -ne [IntPtr]::Zero -and
    [G4WindowRect]::IsWindowVisible($_.MainWindowHandle)
} | Select-Object -First 1
if ($null -eq $process) { throw 'Cannot find a running visible AutoTest G4 WPF window.' }
[G4WindowRect+RECT] $rect = [G4WindowRect+RECT]::new()
if (-not [G4WindowRect]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) {
    throw 'Win32 GetWindowRect failed.'
}
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 100 -or $height -le 100) { throw 'G4 window rectangle is invalid.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$target = Join-Path (Resolve-Path $OutputDirectory).Path ("G4_" + $Scenario + "_" + $width + "x" + $height + ".png")
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
try {
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0,
            [System.Drawing.Size]::new($width, $height))
    } finally { $graphics.Dispose() }
    $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
} finally { $bitmap.Dispose() }
Write-Host "Captured real Windows window: $target"

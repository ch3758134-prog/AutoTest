# AutoTest full-product pure-UI Windows launch helper.
# Does not connect ECU or modify the formal domain solution.
param([ValidateSet("Debug","Release")][string]$Configuration = "Debug")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src\AutoTest.Desktop\AutoTest.Desktop.csproj"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$log = Join-Path $env:TEMP ("AutoTest-UI-Build-" + $stamp + ".log")
Write-Host "AutoTest 全新纯 UI · Windows 验证" -ForegroundColor Cyan
Write-Host "项目：" $project
Write-Host "构建日志：" $log
Push-Location $root
try {
    $output = & dotnet build $project -c $Configuration -nologo -v minimal 2>&1
    $output | Out-File -FilePath $log -Encoding utf8
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "UI 编译失败。日志：$log" }
    Write-Host "UI 编译通过，启动原生 Windows WPF 应用。" -ForegroundColor Green
    Write-Host "注意：此处仅为纯 UI，绝不表示已通过业务/硬件验收。" -ForegroundColor Yellow
    & dotnet run --project $project -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "UI 进程退出码：$LASTEXITCODE" }
}
finally {
    Pop-Location
}

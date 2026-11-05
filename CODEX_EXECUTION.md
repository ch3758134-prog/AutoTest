# AutoTest WPB-G4-INIT · V0-R2｜Codex 本地机械执行合同

**执行范围：** 仅新建 `D:\01_Projects\AutoTest_HOST` 的模拟 HOST（INTEGRATION/V0）。不操作旧 HOST、不触碰 SAEF 仓库、不接真实 USB/CAN/ECU、不输出 IGN/电磁阀/按键/烧录控制。Codex 只能照抄命令、记录原始结果；**不得研究、改写、修复代码、调整 SDK/Prism/NuGet 版本、删除目录、覆盖文件或擅自提交 Git**。

## 本轮签发依据与保留状态

- 主对话已批准：SDK 改为 `10.0.400/latestPatch`；Prism Community License 资格已确认；原 11 个新增路径纳入 V0；CT-01～CT-23 可以作为**候选接口**进入编译验证。
- 尚未完成：Windows Restore、Build、Test、WPF 实机验收、CONTRACT_REVISION 正式冻结、HOST Git 首次提交。G4/G5 Gate 仍 `NOT_REVIEWED`。
- 已知机器：Windows NT 10.0.26200.0；SDK 10.0.400；WindowsDesktop Runtime 10.0.11；Git 2.55.0.windows.2。`dotnet --info` 曾抛出 `System.TypeInitializationException` / 内部 `NullReferenceException`，**必须记录，但不作为单独停止依据**。
- **本包签发 SHA-256：** `__APPROVED_ZIP_SHA256__`。如该位置仍是占位符，不得执行。采用与 ZIP 一起交付的独立版《V0-R2 Codex 中文执行合同》，其中已填写确切 SHA；不要直接执行 ZIP 内未签名模板。新 ZIP 的 SHA 和文件清单应向主对话回传。
- 固定文件名：`AutoTest_WPB_G4_INIT_V0_R2_Source_Delivery.zip`。由用户把完整 ZIP 放在下述 `$ZipPath`；不要使用旧的 V0 ZIP。

## A. 仅读预检（不解压、不创建 HOST、不还原依赖）

在 **PowerShell** 中一次粘贴整块。输出中每个 `PASS/STOP` 和退出码都须原样回传；`STOP` 后立即结束，**不要修复**。

```powershell
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$HostRoot = 'D:\01_Projects\AutoTest_HOST'
$ZipPath = 'D:\01_Projects\AutoTest_WPB_G4_INIT_V0_R2_Source_Delivery.zip'
$ApprovedZipSha = '__APPROVED_ZIP_SHA256__'
$V0PreflightPassed = $false
# 避免调用位置恰有其他工程的 global.json 影响 A 阶段 SDK 观测
Set-Location -LiteralPath $env:TEMP
if ($ApprovedZipSha -notmatch '^[A-Fa-f0-9]{64}$') { throw 'STOP: APPROVED_ZIP_SHA_NOT_SET' }
if (!(Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw 'STOP: ZIP_NOT_FOUND' }
$Digest = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
'ZIP_SHA256=' + $Digest
if ($Digest -ne $ApprovedZipSha.ToLowerInvariant()) { throw 'STOP: ZIP_SHA_MISMATCH' }
'OS=' + [System.Environment]::OSVersion.VersionString
$Dotnet = Get-Command dotnet -ErrorAction Stop
$Git = Get-Command git -ErrorAction Stop
'DOTNET_PATH=' + $Dotnet.Source
'GIT_PATH=' + $Git.Source
& git --version
if ($LASTEXITCODE -ne 0) { throw 'STOP: GIT_VERSION_FAILED' }
& dotnet --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'STOP: SDK_LIST_FAILED' }
$Runtimes = & dotnet --list-runtimes
$ListRuntimeExit = $LASTEXITCODE
$Runtimes
if ($ListRuntimeExit -ne 0) { throw 'STOP: RUNTIME_LIST_FAILED' }
if (-not ($Runtimes -match '^Microsoft\.WindowsDesktop\.App\s+10\.0\.\d+\s')) { throw 'STOP: WINDOWS_DESKTOP_RUNTIME_10_MISSING' }
# 仅记录 --info 的异常和退出码；它不会代替真正的版本、MSBuild、Restore/Build/Test 门禁。
$SavedPref = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    'DOTNET_INFO_BEGIN'
    & dotnet --info 2>&1
    'DOTNET_INFO_EXIT=' + $LASTEXITCODE
} catch {
    'DOTNET_INFO_EXCEPTION=' + $_.Exception.ToString()
} finally {
    $ErrorActionPreference = $SavedPref
}
$Sdk = (& dotnet --version 2>&1 | Out-String).Trim()
$VersionExit = $LASTEXITCODE
'DOTNET_VERSION=' + $Sdk
'DOTNET_VERSION_EXIT=' + $VersionExit
if ($VersionExit -ne 0 -or $Sdk -notmatch '^10\.0\.4\d\d$') { throw 'STOP: SDK_400_BAND_NOT_WORKING' }
'MSBUILD_BEGIN'
& dotnet msbuild -version -nologo
$MsbuildExit = $LASTEXITCODE
'MSBUILD_EXIT=' + $MsbuildExit
if ($MsbuildExit -ne 0) { throw 'STOP: MSBUILD_FAILED' }
if (Test-Path -LiteralPath $HostRoot) {
    if (!(Test-Path -LiteralPath $HostRoot -PathType Container)) { throw 'STOP: HOST_ROOT_NOT_DIRECTORY' }
    if (@(Get-ChildItem -LiteralPath $HostRoot -Force).Count -gt 0) { throw 'STOP: HOST_ROOT_NOT_EMPTY' }
}
'HOST_ROOT_EXISTS=' + (Test-Path -LiteralPath $HostRoot)
$Ancestor = Split-Path -Path $HostRoot -Parent
while ($Ancestor -and (Test-Path -LiteralPath $Ancestor)) {
    $GitTop = & git -C $Ancestor rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0) { throw ('STOP: ANCESTOR_GIT ' + $GitTop) }
    $Next = Split-Path -Path $Ancestor -Parent
    if ($Next -eq $Ancestor) { break }
    $Ancestor = $Next
}
Add-Type -AssemblyName System.IO.Compression
# Windows PowerShell 5.1 的 ZipFile 类位于 FileSystem 程序集中
Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
$Zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $Names = @($Zip.Entries | ForEach-Object { $_.FullName })
    if ($Names.Count -ne 64) { throw 'STOP: ARCHIVE_FILE_COUNT_NOT_64' }
    if (@($Names | Select-Object -Unique).Count -ne $Names.Count) { throw 'STOP: ARCHIVE_DUPLICATE_PATHS' }
    foreach ($Name in $Names) {
        if ($Name -match '\\|^/|^[A-Za-z]:|(^|/)\.\.(/|$)') { throw ('STOP: UNSAFE_ZIP_ENTRY ' + $Name) }
    }
    $Entry = $Zip.GetEntry('global.json')
    if ($null -eq $Entry) { throw 'STOP: GLOBAL_JSON_MISSING' }
    $Reader = [System.IO.StreamReader]::new($Entry.Open())
    try { $Config = $Reader.ReadToEnd() | ConvertFrom-Json }
    finally { $Reader.Dispose() }
    if ($Config.sdk.version -ne '10.0.400' -or $Config.sdk.rollForward -ne 'latestPatch' -or $Config.sdk.allowPrerelease -ne $false) { throw 'STOP: GLOBAL_JSON_MISMATCH' }
    if ($null -eq $Zip.GetEntry('FILE_SHA256_MANIFEST.json')) { throw 'STOP: MANIFEST_MISSING' }
} finally { $Zip.Dispose() }
$V0PreflightPassed = $true
'V0_R2_READONLY_PREFLIGHT_PASS'
```

A 阶段所有关键检查通过才可进入 B；如果 `dotnet --info` 异常而 `dotnet --version` 和 MSBuild 成功，**继续 B 进行实际 Restore/Build/Test 验证**；若任一关键检查失败，停止并回传。

## B. 解包与 Windows Restore / Build / Test（A 通过后才可执行）

**必须沿用同一个 PowerShell 会话。** 不得覆盖现有非空 HOST、不得改动源码或自动修复。`Expand-Archive` 是首次源码写入操作；NuGet 和构建生成的缓存、`obj/bin/TestResults` 属于工具正常产物，不能被当作新增生产源码。

```powershell
$ErrorActionPreference = 'Stop'
if ($V0PreflightPassed -ne $true) { throw 'STOP: READONLY_PREFLIGHT_NOT_PASSED' }
if (!(Test-Path -LiteralPath $ZipPath -PathType Leaf)) { throw 'STOP: ZIP_MISSING' }
if ((Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $ApprovedZipSha.ToLowerInvariant()) { throw 'STOP: ZIP_CHANGED' }
if (Test-Path -LiteralPath $HostRoot) {
    if (!(Test-Path -LiteralPath $HostRoot -PathType Container)) { throw 'STOP: HOST_ROOT_NOT_DIRECTORY' }
    if (@(Get-ChildItem -LiteralPath $HostRoot -Force).Count -ne 0) { throw 'STOP: HOST_ROOT_NOT_EMPTY' }
}
# 先再次核对祖先 Git 后才允许创建 HOST，避免在错误目录下留下新文件
$Ancestor = Split-Path -Path $HostRoot -Parent
while ($Ancestor -and (Test-Path -LiteralPath $Ancestor)) {
    $GitTop = & git -C $Ancestor rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0) { throw ('STOP: ANCESTOR_GIT ' + $GitTop) }
    $Next = Split-Path -Path $Ancestor -Parent
    if ($Next -eq $Ancestor) { break }
    $Ancestor = $Next
}
if (!(Test-Path -LiteralPath $HostRoot)) { New-Item -ItemType Directory -Path $HostRoot | Out-Null }
Expand-Archive -LiteralPath $ZipPath -DestinationPath $HostRoot
$ManifestPath = Join-Path $HostRoot 'FILE_SHA256_MANIFEST.json'
$Manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (@($Manifest.files).Count -ne 63 -or $Manifest.authorized_file_count -ne 53 -or $Manifest.approved_extra_file_count -ne 11) { throw 'STOP: MANIFEST_SCOPE_COUNT_MISMATCH' }
foreach ($File in $Manifest.files) {
    $Target = Join-Path $HostRoot ($File.path.Replace('/', '\'))
    if (!(Test-Path -LiteralPath $Target -PathType Leaf)) { throw ('STOP: FILE_MISSING ' + $File.path) }
    $ActualHash = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($ActualHash -ne $File.sha256.ToLowerInvariant()) { throw ('STOP: FILE_HASH_MISMATCH ' + $File.path) }
}
$Expected = @($Manifest.files | ForEach-Object { $_.path }) + 'FILE_SHA256_MANIFEST.json'
$Actual = @(Get-ChildItem -LiteralPath $HostRoot -File -Recurse | ForEach-Object { $_.FullName.Substring($HostRoot.Length + 1).Replace('\','/') })
if (Compare-Object ($Expected | Sort-Object) ($Actual | Sort-Object)) { throw 'STOP: UNEXPECTED_OR_MISSING_EXTRACTED_FILES' }
'PAYLOAD_64_FILES_HASH_VERIFIED'
Set-Location -LiteralPath $HostRoot
$Sdk = (& dotnet --version 2>&1 | Out-String).Trim()
$VersionExit = $LASTEXITCODE
'DOTNET_SDK_IN_HOST=' + $Sdk
'DOTNET_SDK_IN_HOST_EXIT=' + $VersionExit
if ($VersionExit -ne 0 -or $Sdk -notmatch '^10\.0\.4\d\d$') { throw 'STOP: GLOBAL_JSON_SDK_SELECTION_FAILED' }
& dotnet msbuild -version -nologo
'MSBUILD_IN_HOST_EXIT=' + $LASTEXITCODE
if ($LASTEXITCODE -ne 0) { throw 'STOP: MSBUILD_IN_HOST_FAILED' }
'RESTORE_SOLUTION_BEGIN'
& dotnet restore .\AutoTest.sln --configfile .\NuGet.Config
$RestoreExit = $LASTEXITCODE
'RESTORE_SOLUTION_EXIT=' + $RestoreExit
if ($RestoreExit -ne 0) { throw 'STOP: RESTORE_SOLUTION_FAILED' }
'RESTORE_NEGATIVE_SAMPLE_BEGIN'
& dotnet restore .\tests\negative\StrongIdMisuse\StrongIdMisuse.csproj --configfile .\NuGet.Config
$NegRestoreExit = $LASTEXITCODE
'RESTORE_NEGATIVE_SAMPLE_EXIT=' + $NegRestoreExit
if ($NegRestoreExit -ne 0) { throw 'STOP: RESTORE_NEGATIVE_SAMPLE_FAILED' }
'BUILD_RELEASE_BEGIN'
& dotnet build .\AutoTest.sln -c Release --no-restore
$BuildExit = $LASTEXITCODE
'BUILD_RELEASE_EXIT=' + $BuildExit
if ($BuildExit -ne 0) { throw 'STOP: BUILD_RELEASE_FAILED' }
'TEST_RELEASE_BEGIN'
& dotnet test .\AutoTest.sln -c Release --no-build --no-restore --logger 'trx;LogFileName=V0.trx'
$TestExit = $LASTEXITCODE
'TEST_RELEASE_EXIT=' + $TestExit
if ($TestExit -ne 0) { throw 'STOP: TEST_RELEASE_FAILED' }
'V0_R2_RESTORE_BUILD_TEST_COMMANDS_PASSED_PENDING_RESULT_REVIEW'
```

**特别约束：** 测试集内的 `G3_0_T01_CompileNegative` 会单独编译 `tests/negative/StrongIdMisuse/StrongIdMisuse.csproj` 并预期看到 `CS1503`，故负编译样例**不加入解决方案**，不要直接对它要求成功 Build，也不能通过修正 `BadId.cs` 使其通过。各 `dotnet` 步骤必须记录原始 stdout/stderr、退出码、测试数与异常；任何失败立即停止，不得重复试错或由 Codex 修复。

## C. WPF 模拟人工验收（B 全通过后；无真实硬件接线）

由操作员确保无任何真实台架 USB 连接。在 `$HostRoot` 执行：

```powershell
& dotnet run --project .\src\AutoTest.Desktop\AutoTest.Desktop.csproj -c Release --no-build --no-restore
'WPF_RUN_EXIT=' + $LASTEXITCODE
```

核对 Prism 主窗口实际显示、模拟命令按钮和独立的 REQUEST / MOCK ACK / MOCK OBSERVATION / INCONCLUSIVE 判定展示；依次执行“正常模拟、模拟超时、模拟断连、无授权拒绝”。失败路径不得虚构 ACK/观测/物理 PASS。**截图和人工结果须如实回传**。如果启动失败或异常，停止；不更改 Prism 配置/XAML、不得修复。

## D. Git 首次提交（独立后置授权；本轮不得自动执行）

只有主对话审查 B/C 的完整输出并另行明确授权后，才允许在新的 `AutoTest_HOST` 仓库执行 `git init -b main`、`git add --all`、`git diff --cached --check`、`git commit`、`git rev-parse HEAD`、`git status --porcelain`。禁止 `reset/clean/force`；不得在 SAEF Git 提交。`CONTRACT_REVISION` 与 Gate 状态也只能由主对话后续裁决。

## E. Codex 原样回传格式

- A：Windows/SDK/Runtime/Git、zip SHA、原始 `dotnet --info` 异常（如有）、`dotnet --version`、MSBuild 版本与各退出码、HOST/祖先 Git 状态、`V0_R2_READONLY_PREFLIGHT_PASS` 是否出现。
- B：解压前检查、64 文件哈希、SDK 选择、解决方案 Restore、负样例 Restore、Build、Test **每条命令原始 stdout/stderr 和退出码**，测试通过/失败/跳过数量、TRX 路径。
- C：WPF 运行结果、截图、各模拟动作的可见事实；是否有真实设备连接（应为否）。
- D：默认 `NOT_AUTHORIZED / NOT_RUN`；HOST 首次提交、合同正式冻结、G4/G5 Gate 一律不得因 B 成功自动宣称通过。

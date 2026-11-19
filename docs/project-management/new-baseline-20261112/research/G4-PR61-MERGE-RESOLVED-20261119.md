> **最新合并状态覆盖旧记录（2026-11-19）**：PR #61 后续已正式合并进 main，当前主线 `eb1b74a3d7eeb6dcbb582d9c04c72f97f5e1204e`。本文件此前“main 未修改、尚未合并”的语句仅是中间过程快照。详见 [G4-PR61-MAIN-MERGED-20261119.md](G4-PR61-MAIN-MERGED-20261119.md)。Windows 构建/WPF 启动和现场功能验收仍未验证。

# AutoTest｜G4 PR #61 冲突已解决：集成候选验收记录

**日期**：2026-11-19  
**当前状态**：`GIT_MERGE_RESOLVED / PR_MERGEABLE / WINDOWS_BUILD_NOT_RUN / MAIN_NOT_UPDATED / FIELD_G4_PARTIAL`  
**范围**：集成源码冲突消解及候选生成；不代替 Windows WPF 编译，不代替完整 G4 真实自动测试验证。

## 1. 本次已经执行的 GitHub 实际操作

- 三方共同祖先：`4831ea2a01cce4112de063602534707aa55b727a`
- main 基准：`1d6eeba8561a779640b20c5a3f384327a0018079`
- 原 G4 HEAD：`ce4dbd4401dac1a98ffa87885865d0b7c174df7f`
- 三份 **完整递归 Git tree** 均未截断；按 blob SHA 进行差异分析，发现 5 个双边文件冲突。
- 以 main 的 tree 为基线，吸收 237 项 G4 文件变更（169 新增、66 修改、2 删除）。对 5 项同名文件冲突，经对照 base/main/G4 后选用同时兼容 main 旧 Mock 删除的 G4 版本。完成后 608 个文件路径+blob SHA 与独立预期映射全部一致，0 mismatch、0 文件/目录前缀碰撞。
- 双父 merge commit：`9d01281719d1cabe54efbaaaaafc33fcadc125f4`，parents=`G4原HEAD, main原HEAD`。集成 tree：`42b2c5392b434c335ddf0c42ee7b809f06e00c68`。
- 冲突报告：`docs/integration/G4_PR61_MERGE_RESOLUTION_20261119.md`。
- 最终 G4 HEAD（加冲突报告）：`92c6cb46a35ae11c1ffdefc0e0ece9339906d21a`。
- 受控复核分支：`integration/g4-pr61-resolved-20261119@92c6cb46a35ae11c1ffdefc0e0ece9339906d21a`，已使用 `expected_sha` 租约将 `feature/g4-html-parity-20261115` 快进到同一个 SHA。
- [PR #61](https://github.com/ch3758134-prog/AutoTest/pull/61) GitHub 回读：`mergeable=true`，`open`、`draft`、`merged=false`，`behind main=0`，PR 238 个文件（多 1 个审计报告）。
- **main 未修改**：仍 `1d6eeba8561a779640b20c5a3f384327a0018079`。

## 2. 五处冲突处理

| 路径 | 决定 | 验证依据 |
|---|---|---|
| `src/AutoTest.Desktop/Testing/StepUiCatalog.cs` | G4 | main 删除旧 Assert UI；G4 亦删除，并提供新步骤目录 |
| `src/AutoTest.Testing/Authoring/StepDescriptorCatalog.cs` | G4 | G4 不恢复 MockObserve/旧 Assert，增加真实步骤与 CallPublicFlow BLOCKED |
| `src/AutoTest.Testing/Authoring/StepTreeValidator.cs` | G4 | G4 删除旧 MockQuality 检查并增加资源/流程约束 |
| `tests/AutoTest.Desktop.Tests/Projects/TestEditorWorkflowTests.cs` | G4 | G4 不再使用旧 Assert fallback |
| `tests/AutoTest.Testing.Tests/AuthoringAndRuntimeTests.cs` | G4 | G4 不再引用 MockObserve/MockStation，使用正式测试作者模型与运行快照测试 |

**本次不是全仓采用 ours/theirs**；只在 5 个真实冲突处作了定点决策；所有 main 独立文件和所有 G4 非冲突变更均进入候选。

## 3. 剩余的必须验证事项

**本次环境无法运行 Windows WPF 构建**：容器无 `dotnet` SDK 且不是 Windows。不能写“Windows Build PASS”、“TEST 通过”或“真机通过”。合并后的新文件虽然哈希完整一致，仍可能存在跨文件 API/编译/项目引用不兼容问题。

请 Windows Codex 使用准确 `92c6cb46a35ae11c1ffdefc0e0ece9339906d21a`：
```powershell
git fetch origin
git switch feature/g4-html-parity-20261115
git pull --ff-only
git rev-parse HEAD
dotnet restore AutoTest.sln
dotnet build AutoTest.sln -c Debug --no-restore
dotnet test tests/AutoTest.Testing.Tests/AutoTest.Testing.Tests.csproj -c Debug --no-restore
dotnet test tests/AutoTest.Devices.Tests/AutoTest.Devices.Tests.csproj -c Debug --no-restore
dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Debug --no-restore
```
测试通过后在 Windows 启动正式 `AutoTest.Desktop`，记录启动与编辑/保存/工程打开结果。任何失败原样交回对应专业修正，不能把 Mock 当作真实设备测试证据。

真实 KEY、CallPublicFlow、主控新 GPIO/LED、FLASH 主 HOST USB 移交等独立实测能力继续保持准确的未验限制。新的 Fluent Light UI 设计仍由 `ui/ide-design` 主责，现行 G4 UI 不自动视为最终视觉验收。

## 4. 下一门禁

- 技术冲突：**CLOSED**；
- Windows WPF 构建与可启动：**PENDING**；
- G4 阶段源码合入 main：**PENDING**（必须通过构建/安全兼容审查后批准）；
- G4 整体现场实测：**NOT_FULLY_VERIFIED**。

**管理任务**：[Issue #62](https://github.com/ch3758134-prog/AutoTest/issues/62)；原批准方向：[G4 阶段源码接收与现代 UI 交接](G4-PR61-BASELINE-ACCEPTANCE-20261119.md)。

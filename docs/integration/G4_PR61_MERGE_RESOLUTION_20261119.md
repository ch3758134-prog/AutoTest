# AutoTest｜PR #61 G4 → HOST main 受控冲突解决记录

**执行日期**：2026-11-19。**性质**：源码三方 Git 树冲突解决与候选交付，不是 Windows Build/真实 ECU 功能验收。  
**管理任务**：[Issue #62](https://github.com/ch3758134-prog/AutoTest/issues/62)，原始 [Draft PR #61](https://github.com/ch3758134-prog/AutoTest/pull/61)。  
**主线**：`main@1d6eeba8561a779640b20c5a3f384327a0018079`。  
**原候选**：`feature/g4-html-parity-20261115@ce4dbd4401dac1a98ffa87885865d0b7c174df7f`。  
**共同祖先**：`4831ea2a01cce4112de063602534707aa55b727a`。  
**解决后的双父提交**：`9d01281719d1cabe54efbaaaaafc33fcadc125f4`（父1：G4 HEAD，父2：main HEAD）。  
**解决后的 tree**：`42b2c5392b434c335ddf0c42ee7b809f06e00c68`。

## 1. 实际执行方法

通过 GitHub 原生 Git Tree API 读取 ancestor/main/G4 的**完整递归树**，三份均 `truncated=false`，分别为 828 / 539 / 1034 个 Git tree 目录与文件节点。逐个文件 SHA 比较进行三方 merge，保留：

- `main` 相对于共同祖先独有的全部修改和文件；
- G4 相对于共同祖先独有的全部修改和文件；
- 对于5个双方均修改的内容冲突，逐项审查后使用 G4 版本，因其也保留了 main 删除旧 Mock/Assert 入口的决定；
- 包括 G4 显式删除旧 `StepParametersDialog.xaml` 及其 code-behind，避免恢复过期编辑器。

结果是：总计 **237 个相对 main 的文件变更**（169 新增、66 现有修改、2 删除）；其中 **5 个冲突为人工定点取舍**，其余按祖先差异自动择取。未使用全仓 ours/theirs 覆盖。

使用 `main` 作为基树，通过 GitHub `create_tree` 生成完整候选 tree，再对 **608 个最终 blob** 的路径+SHA 与预期合并映射逐一回读比较，**0 项不一致**；没有任何文件/目录路径前缀冲突或文件 mode 差异。构造双父 merge commit，完成候选分支受控前进。

## 2. 五个冲突的定点取舍

| 文件 | main 的独立修改 | G4 的独立修改与处理理由 | 处理 |
|---|---|---|---|
| `src/AutoTest.Desktop/Testing/StepUiCatalog.cs` | 删除旧 `Assert` 中文 UI 目录入口 | G4 同样删掉旧 Assert，并扩展真实 Write/Read/CAN/WAVE、公共流程、未接线步骤的显示与编辑属性 | **G4** |
| `src/AutoTest.Testing/Authoring/StepDescriptorCatalog.cs` | 删除 `MockObserve` 与旧 `Assert` descriptor | G4 同样移除两项，同时新增实际步骤 descriptor 和 CallPublicFlow **不可执行**定义；不会让流程伪 PASS | **G4** |
| `src/AutoTest.Testing/Authoring/StepTreeValidator.cs` | 移除 `MockObserve` 旧质量枚举校验 | G4 同样移除该校验，加入真实资源绑定、禁用旧步骤、期望/范围/执行条件预检 | **G4** |
| `tests/AutoTest.Desktop.Tests/Projects/TestEditorWorkflowTests.cs` | 将旧 `Assert` fallback 改为 `SetVariable` | G4 同样不再引用已删除的 Assert，且适配新版步骤 UI 选项 | **G4** |
| `tests/AutoTest.Testing.Tests/AuthoringAndRuntimeTests.cs` | 删除 MockObserve/RunCoordinator/MockStation 测试并重写为正式作者模型验证 | G4 同样删除基于 MockObserve 的测试，保留正式步骤/快照/预检等回归测试 | **G4** |

以上五项仅确认**文件级冲突与语义选择**。完整 Windows C# 编译、XAML 编译与测试仍可能发现非同名文件间的 API 或业务兼容问题；必须使用 Windows Codex 实际执行后审查。

## 3. 当前证据与限制

**已核对**：
- 三树 `truncated=false`，路径/Blob SHA 完整；候选 tree 与预期 608 个文件完全一致；
- 候选双父提交 SHA 与两父关系得到 GitHub 回读；
- `compare(main...candidate)` 为 `status=ahead, behind=0`，相对 main 237 文件；
- 原 G4 代码/源文件保留，main 独有代码不会被静默丢弃；
- 没有直接修改 main。

**尚未验证**：
- 当前运行环境没有 `dotnet` SDK，也不是 Windows，**没有进行 Windows WPF Build/Test/启动**，不宣称构建成功或 0 错误；
- 没有进行 USB/ECU/CAN/KEY/夹具/Flash/UDS 实体操作，所有真机测试保持原项目的未验证限制；
- 合并后的 G4 原有公共流程 CallPublicFlow 真实执行及直接 KEY 新合同仍可能 BLOCKED；
- `Main_Control` 最新 GPIO/LED/双夹具接口、`0xF1` 固件身份的真机兼容仍须基于现场镜像实测；
- WPF 全面现代 Fluent UI 属 `ui/ide-design` 专项，不能将合入 G4 旧 UI 当成 Fluent 设计验收。

## 4. 下一道准入

由 Windows 本地 Codex 检出准确候选 SHA，机械执行：`dotnet restore AutoTest.sln`；`dotnet build AutoTest.sln -c Debug --no-restore`；相应 `AutoTest.Testing.Tests`、`AutoTest.Devices.Tests`、`AutoTest.Desktop.Tests` 和 FLASH 相关项目测试（不借 Mock 冒充真机）；启动真正的 `AutoTest.Desktop` WPF。若编译/测试/启动失败，回报原始错误和准确文件，不自动改主线。

**只有构建/源码兼容验证和当前主线接收评审均完成后，再请求将 PR #61 合并 main**；合并只表示受控源码基线接收，不代表所有设备/自动生产测试 PASS。

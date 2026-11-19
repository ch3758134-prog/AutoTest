# AutoTest｜G4 WPF 严格 HTML 视觉与交互整改候选（2026-11-15）

**交付状态：CODE_CANDIDATE / STATIC_CHECK_PASS / WINDOWS_BUILD_UNVERIFIED / DESKTOP_TESTS_UNVERIFIED / HTML_VISUAL_DIFF_OPEN / NOT_GATE_ACCEPTED**

- 目标分支：`feature/g4-html-parity-20261115`；**不写入 main**
- 依据：Issue #53 获批的 Desktop 表现层整改；冻结 HTML `docs/ui/archive/auto-test-20261115-ec75a9fe/AutoTest_UI.html`（Git blob `c1a55e295c0f5321736fa3a8a7db20e13415a727`）；`G3-01-STRICT-UI-PARITY.md`
- 此轮实现基准：`a07f8a40375e2b22eed74ff8b256803a50cd6988`
- 本文生成前最新实现 HEAD：`e221c59553e6a5c69b13665b0fc3ce9f30ddd2a5`；提交本文后请使用**分支最终 SHA**检出和 Windows Build，不要用旧基准
- 变更：25 次增量提交、12 个源码/测试文件（不含本文）。冻结 HTML、`main`、`ui/ide-design`、领域 TEST/Controller/Runtime、Devices/Flash、固件均未修改

## 1. 改动边界与代码导航

| 文件 | 主要入口 / 已改内容 |
|---|---|
| `Shell/MainWindow.xaml.cs` | `OnSourceInitialized` / `WorkAreaWindowProc`：Win32 `WM_GETMINMAXINFO` 根据**当前显示器工作区**计算自定义 WindowChrome 的最大位置/尺寸（不占任务栏）；`OnShellSizeChanged` / `ApplyPanelLayout`：低有效宽/高优先保证编辑区 |
| `Shell/MainWindow.xaml` | 最小窗口约束、可调整左工程树列，与底部/Inspector 自适应 |
| `Testing/TestEditorView.xaml` | 三区阶段配色、步骤类别图标色、35 DIP 步骤行、45 DIP 表头、独立选中/搜索/未完成草稿/真实错误视觉；原生 `ActionPalette` 分组选择 |
| `Testing/TestEditorView.xaml.cs` | `OpenActionPalette`、`RefreshPaletteItems`、`PaletteList_PreviewMouseLeftButtonUp`：分组、搜索、单击即添加；`StepSearch_KeyDown`：Enter/Shift+Enter；`ShowInspector`：属性/常用操作及自动焦点 |
| `Testing/TestEditorViewModel.cs` | `StartInsert` / `ChooseNewStepAction`：类型选定立即 `StepEditTransaction.Insert`；`CancelInspectorEdit`、`Undo`、`Redo`：可撤销草稿；`NavigateSearch` / `RefreshRows`：不删除非命中行；`SaveAsync` / `SaveForCloseAsync`：未完成草稿阻断保存；`RefreshTree`：编辑后移除不再对应当前版本的运行事实 |
| `Testing/TestEditorInteractionState.cs` | `ExpandAncestors`：搜索命中能展开折叠分支、保留稳定 StepId |
| `Testing/StepRowProjection.cs` | `Summary` / `Category` / `Icon` / `State`：工程师摘要、映射类型决定硬件/CAN 类别、状态来自真实 `RealStepFact`；搜索、草稿、断点独立标记 |
| `Testing/TestStepInspector.xaml(.cs)` | `StepActionsPanel` 直达下方插入、复制、上/下移、删除、断点；`PaletteActions` 和 `TestActionOption.Group` 组织冻结 HTML 选择组 |
| `Testing/TestProjectWorkbenchView.xaml` | “本次执行选择”优先、默认启用次级，沿用已批准 `ProjectRunSelectionState` |
| `Testing/StepUiCatalog.cs` | 使用“UDS 服务”正式 UI 术语 |
| `tests/AutoTest.Desktop.Tests/Testing/FrozenStepEditorInteractionTests.cs` | 无模拟设备的正式 TEST 事务草稿、取消、搜索、CASE、状态等代码回归测试；**未执行** |

## 2. 视觉条目 VIS-01～05

| 项 | 源码整改状态 | 证据和未闭合部分 |
|---|---|---|
| VIS-01 准备/测试/清理 | CODE_DONE / VISUAL_UNVERIFIED | HTML `.phase.phase-prepare/test/cleanup` 的蓝灰、青绿、浅金背景和左色带写入对应 DataTrigger；需 Windows 实际高度/边界/文字对照 |
| VIS-02 步骤类型 | CODE_DONE / DOMAIN_VISUAL_UNVERIFIED | CAN 蓝、硬件橙、UDS/流程紫、波形绿、基础中性；图标+文字并用；正式 Write 以 `TestTargetMapping.Kind` 决定设备类别 |
| VIS-03 选中/搜索/草稿/错误 | PARTIAL | 选中蓝左条、搜索黄、草稿浅金、断点图形、真实 StepFact 错误红色已实现；运行时逐步“正在执行”、可执行禁用旗标尚无完整后端数据合同，未捏造 |
| VIS-04 列/行密度 | CODE_DONE / PIXEL_UNVERIFIED | HTML 最终 `.steprow` 35px、表头计算约 45px，已按 DIP 设定；列宽和 100/125/150% DPI 的 WPF 实测仍待证据 |
| VIS-05 字体/适配 | PARTIAL | 优先中央编辑区、窄窗口折叠右/底、左树可缩窄；字体未按窄屏粗暴缩小；整版像素级差异/窗体缩放、空状态密度没有实际 Windows 截图验收 |

## 3. 用户操作条目 UX-01～08

| 项 | 状态 | 对照说明 |
|---|---|---|
| UX-01 新增步骤 | CODE_DONE / GUI_UNVERIFIED | 选中位置 → 原生分组 Palette（CAN与数据/工位操作/UDS服务/波形分析/流程与计算） → 单击类型 → **即刻可撤销 TEST 草稿行** → 当前 Inspector 编辑。无二次“插入”搜索入口；未填必要参数不能落盘 |
| UX-02 搜索 | CODE_DONE / GUI_UNVERIFIED | 不过滤完整树；黄色命中不同于蓝色选中；Enter/Shift+Enter 下一/上一，展开祖先；命中数与高亮共用条件 |
| UX-03 Inspector 高频操作 | PARTIAL | 名称、真实专业参数、下方插入、复制、上/下移、删除、编辑断点；真实执行启用标志没有正式 DSL 时只告知，不加无效假开关 |
| UX-04 人类摘要 | CODE_DONE / RESOURCE_VERIFY_OPEN | Wait/Loop/IF/WHEN、IGN/KEY、DBC 写报文、Read、Wave/UDS 等中文摘要，长串截断。部分高级 CAN/Read 新语义因后端合同未批准只能明确占位 |
| UX-05 流程树操作 | EXISTING + UPDATED / GUI_UNVERIFIED | `StepInsertionTarget` 的 sibling 与 Slot、CASE-only 限制保留；新草稿撤销/重做仍标记未完成；IF/ELSE、SWITCH/CASE、TRY/子程序嵌套在 Windows 上尚未跑全矩阵 |
| UX-06 键鼠 | CODE_DONE / GUI_UNVERIFIED | Palette 一次鼠标点击选择、双击、Enter、搜索键盘导航；原有 F2/F9、Ctrl+S/Z/Y 等继续使用；焦点已改为优先新步骤 Inspector |
| UX-07 项目/工程树同步 | EXISTING + VISUAL_TUNE / GUI_UNVERIFIED | 共用 `ProjectRunSelectionState`，本次选择与持久 Enabled 不混淆；项目修订/SHA 仍需显式采用，重复打开与 30 用例场景待 Windows 实测 |
| UX-08 保存/关闭/撤销 | CODE_DONE / GUI_UNVERIFIED | 草稿不能无校验落盘/执行；取消插入直接 Undo，不再增加反向 Delete 污染历史；Ctrl+Y 恢复时仍标记待完成；退出复用现有 Save/Discard/Cancel |

## 4. HTML → WPF 差异登记（未经 Windows 观察不得关闭）

| UI-DIFF-ID | HTML 依据 | WPF 对应 | 证据状态 |
|---|---|---|---|
| UI-DIFF-01 | 全屏编辑必须处于系统可见工作区 | `WM_GETMINMAXINFO` 映射 monitor Work/Monitor | WINDOWS_UNVERIFIED；任务栏上下/左右/多屏待核查 |
| UI-DIFF-02 | `.phase.phase-prepare/test/cleanup` | `TestEditorView.xaml` 三色 DataTrigger、标题字重/色带 | VISUAL_UNVERIFIED |
| UI-DIFF-03 | `.steprow.active`、`.step-match` 互不混淆 | `ListBoxItem` 选中背景、左色条、搜索黄、草稿浅金 | VISUAL_UNVERIFIED |
| UI-DIFF-04 | `palette(mode...)` 按五类分组、`insertStep(kind)` 单选即添加 | `ActionPalette` `GroupStyle`、单击调用 `ChooseNewStepAction`，只提交真实 `StepEditTransaction` 草稿 | GUI_UNVERIFIED |
| UI-DIFF-05 | `renderInspector()` 高频操作紧贴参数 | `StepActionsPanel` 6 类就地操作；Enabled 缺正式运行合同，明确展示限制 | PARTIAL |
| UI-DIFF-06 | 原位搜索，Enter/Shift+Enter | `NavigateSearch` 和 `ExpandAncestors` + 独立高亮 | GUI_UNVERIFIED |
| UI-DIFF-07 | 支持调整工程树、属性、底部观察；缩放不裁切 | 自适应 Dock + 可调宽度；默认最大化 WorkArea | DPI_MATRIX_UNVERIFIED |
| UI-DIFF-08 | 当前运行步骤/结果/错误/禁用分开 | 真实 `RealStepFact` 用于已执行状态，未完成执行器数据不假造 | OPEN_BACKEND_STATE |

## 5. 未获授权或不可凭 UI 声称已接线

1. **KEY-SEQ-01**：任意长度 KEY_SIM 20 开关序列与共享命名动作资源；当前 UI 仅真实支持既有单次控制合同，不展示假序列执行。
2. **CAN-WAIT-01**：只按 CAN ID 等待新帧，DBC 可选；现有 `CanWait` 仅为不可执行编辑合同。
3. **READ-STEP-01**：灵活 Latest/Next、可选变量/比较和判定语义，需正式 TEST/Runtime 批准。
4. **FIXTURE-STEP-01**：夹具动作需接真实设备、安全授权及正式 TEST DSL。当前不新增不能真正执行的假按钮。
5. Run/Stop 的项目级真实运行编排、全量流程控制及步骤 Enabled 合同也未由本次改动擅自扩展。

## 6. 验证门槛（机械执行，不要自主修代码）

**事实：** GitHub 已落代码；在此环境无法运行 Windows/.NET WPF，故当前 *Build、Test、启动、HTML 浏览器与 WPF 同分辨率截图、1366×768/1920×1080/2560×1440 DPI 矩阵全部未执行*。不能宣称 Gate PASS。

本地 Codex 只需检出最终精确 SHA，`dotnet build src/AutoTest.Desktop/AutoTest.Desktop.csproj -c Debug`，通过后测试 `dotnet test tests/AutoTest.Desktop.Tests/AutoTest.Desktop.Tests.csproj -c Debug --no-restore`，通过后再运行正式 WPF。不得修改源码、自行加测试替代真实运行、合并 main、触发任何真实 ECU/CAN/KEY/IGN/夹具/烧录命令。

Windows GUI 逐项验收：新建工程/项目/用例 → 阶段添加多种 STEP → 右侧编辑/取消/撤销/重做 → 旧属性未应用时切行/切页 → IF/ELSE/CASE/Loop 插入 → 搜索 Enter/Shift+Enter → Ctrl+S、重开并核对 Rev/SHA → 本次用例勾选/采用引用 → 关窗是否避开任务栏 → 各 DPI 全屏与缩放截图。所有不一致登记新修复 SHA/操作路径；未测标记 UNVERIFIED。

**结论：授权的视觉与操作整改形成候选，静态审查完成；缺 Windows Build/实测与像素级 HTML 对照，G4 Gate 仍不得准出。**

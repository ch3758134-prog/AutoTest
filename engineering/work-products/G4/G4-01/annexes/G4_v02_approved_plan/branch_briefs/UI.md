# G4 v0.2 · 并行方向 `UI` 完整交接候选

> **G4-PREP / NOT_AUTHORIZED**。不得以此文件直接进行编码；只有你批准工作包且V0合同基线有效后，才从本候选转换为正式分支授权单。

## 1. 输入身份和责任

- G3 v0.2批准HTML SHA: `4711733cc2b7e5d125aadce9a97e574d877f4e4825d041f3eaaf8f778f35be46`；SAEF文档HEAD（回执）`5de99332170d9434c7e5b4a15c9d091245a7302c`。
- 新HOST源码仓库：NOT_CREATED；源码BASE_COMMIT：UNSET；公开合同Revision：UNSET。
- 本方向原G3文件责任：**23项**；新增工程/测试责任：**1项**；关联验证：**13项**。
- 非本方向文件即使见于完整工程，也是只读；一切`.csproj`、源码根配置、Composition均由INTEGRATION唯一写入。

## 2. 首批实用目标与交付

该方向必须在其规定波次提交**可编译的业务源码、对应测试源码、样例输入、实际运行状态/NOT_RUN**；不得给出空类或仅界面展示当作完成。每次授权写入的精确路径由主对话发放，而不是默认开放以下所有文件。

## 3. G3已规划的文件责任

- `src/AutoTest.Desktop/Themes/Tokens/Colors.xaml` — ColorTokens；关键方法：`Primary/Surface/Status`；基础色与状态色分离。
- `src/AutoTest.Desktop/Themes/Tokens/Typography.xaml` — TypographyTokens；关键方法：`FontSize/Weight`；导航/内容/属性/表格字体体系。
- `src/AutoTest.Desktop/Themes/Tokens/Metrics.xaml` — MetricTokens；关键方法：`Compact/Normal`；高密度UI尺寸。
- `src/AutoTest.Desktop/Themes/Light.xaml` — LightTheme；关键方法：`MergedDictionaries`；浅色主题资源映射。
- `src/AutoTest.Desktop/Themes/Dark.xaml` — DarkTheme；关键方法：`MergedDictionaries`；深色主题资源映射。
- `src/AutoTest.Desktop/Themes/Controls/TreeStyles.xaml` — TreeStyles；关键方法：`Selection/Hover/RunState`；工程树/步骤树视觉状态。
- `src/AutoTest.Desktop/Themes/Controls/Inputs.xaml` — InputStyles；关键方法：`Validation/Disabled`；字段编辑视觉及错误反馈。
- `src/AutoTest.Desktop/Themes/Controls/Tables.xaml` — TableStyles；关键方法：`RowVirtualization`；CAN日志/结果/统计高密度表。
- `src/AutoTest.Desktop/Themes/Controls/RunBadges.xaml` — RunBadges；关键方法：`Passed/Failed/Unknown`；状态图标和文字配合。
- `src/AutoTest.Desktop/DesignGallery/GalleryWindow.xaml` — GalleryWindow；关键方法：`PreviewTheme/PreviewDensity`；组件/文字/状态可视化验收。
- `src/AutoTest.Desktop/App.xaml` — App；关键方法：`Startup/Resources`；唯一WPF启动和Theme字典。
- `src/AutoTest.Desktop/Bootstrap/PrismRegistration.cs` — PrismRegistration；关键方法：`RegisterTypes/CreateShell`；Prism 9导航、容器组合。
- `src/AutoTest.Desktop/Shell/MainWindow.xaml` — MainWindow；关键方法：`Document/Left/Right/BottomRegions`；IDE工作台。
- `src/AutoTest.Desktop/Shell/MainWindowViewModel.cs` — MainWindowViewModel；关键方法：`OpenCommand/RunCommand`；只发送应用命令。
- `src/AutoTest.Desktop/Shell/WorkbenchLayoutService.cs` — WorkbenchLayoutService；关键方法：`SaveLayout/RestoreLayout`；窗口与Dock状态保存。
- `src/AutoTest.Desktop/Shell/SelectionContext.cs` — SelectionContext；关键方法：`Select/GetSelection`；选中项与当前运行步骤分离。
- `src/AutoTest.Desktop/Project/ProjectExplorerView.xaml` — ProjectExplorerView；关键方法：`AddResource/Rename/Delete`；工程导航和操作。
- `src/AutoTest.Desktop/Testing/TestEditorView.xaml` — TestEditorView；关键方法：`SelectStep/InsertStep`；树/步骤双击及右键编辑。
- `src/AutoTest.Desktop/Testing/StepInspectorView.xaml` — StepInspectorView；关键方法：`EditProperty/Validate`；右侧属性编辑和即时诊断。
- `src/AutoTest.Desktop/Testing/RunMonitorView.xaml` — RunMonitorView；关键方法：`ObserveRun/ShowEvidence`；预期/实际/判定/步骤状态。
- `src/AutoTest.Desktop/Devices/BenchDiagnosticsView.xaml` — BenchDiagnosticsView；关键方法：`Connect/ShowRawFrames`；USB及台架复验入口。
- `src/AutoTest.Desktop/Tools/ToolWindowRegistry.cs` — ToolWindowRegistry；关键方法：`OpenCan/OpenUds/OpenFlash/OpenWave`；共享专业窗口生命周期。
- `src/AutoTest.Desktop/Production/ProductionWorkspaceView.xaml` — ProductionWorkspaceView；关键方法：`Capture/Preflight/Run`；生产工作区与结果。

## 4. G4新增的工程/测试文件责任

- `tests/AutoTest.Desktop.Tests/WorkbenchViewModelTests.cs` — 模块测试代码 / V1；该方向源码和测试同步交付；精确测试ID映射另见101清单。

## 5. 旧源码可利用的候选实现（全部未迁移）

- `src/AutoTest.QuickDesktop/Uds/UdsDebugViewModel.cs` → `src/AutoTest.Desktop/Tools/UdsToolView.xaml`；策略 `REFERENCE_ONLY`；必须验证：Interaction model reference; do not copy old shell。
- `src/AutoTest.QuickDesktop/Dbc/DbcDocumentViewModel.cs` → `src/AutoTest.Desktop/Project/ProjectExplorerView.xaml`；策略 `REFERENCE_ONLY`；必须验证：DBC interaction only; Prism regions and selection re-design。
- `src/AutoTest.QuickDesktop/Waveform/SignalAnalyzerViewModel.cs` → `src/AutoTest.Desktop/Tools/WaveToolView.xaml`；策略 `REFERENCE_ONLY`；必须验证：Wave UI behavior reference; no old window copy。

## 6. 验证目标（必须随代码交付，以下尚未执行）

- `D1-T01` 浅深主题控件画廊 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`DesignTokenViewTests.Verify_D1_T01`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D1-T02` 各交互状态截图 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`DesignTokenViewTests.Verify_D1_T02`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D1-T03` 125%与150%DPI — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`DesignTokenViewTests.Verify_D1_T03`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D1-T04` 空态及长文字 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`DesignTokenViewTests.Verify_D1_T04`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D1-T05` 高对比文字 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`DesignTokenViewTests.Verify_D1_T05`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D1-T06` 主题切换且业务状态不变 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`DesignTokenViewTests.Verify_D1_T06`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D2-T01` WPF启动与导航 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`WorkbenchInteractionTests.Verify_D2_T01`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D2-T02` 新增资源/步骤两种菜单 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`WorkbenchInteractionTests.Verify_D2_T02`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D2-T03` 选中与运行高亮独立 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`WorkbenchInteractionTests.Verify_D2_T03`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D2-T04` 属性编辑错误 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`WorkbenchInteractionTests.Verify_D2_T04`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D2-T05` 双击编辑快捷键 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`WorkbenchInteractionTests.Verify_D2_T05`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D2-T06` CAN/UDS/Flash/Wave窗口打开关闭 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`WorkbenchInteractionTests.Verify_D2_T06`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。
- `D2-T07` 生产扫码视图焦点 — `WPF_MANUAL_SCREENSHOT`；候选类/程序：`WorkbenchInteractionTests.Verify_D2_T07`；前置：需Windows真实WPF截图/操作记录；不可仅用HTML视图。

## 7. 分支停止条件和回传

- 任意接口缺类型/新API语义不符：STOP，报CT和编译位置，由INTEGRATION修订合同后再继续。
- 任意真实设备、固件或烧录授权缺失：STOP，切换到可复现Mock，不用假设备反馈冒充现场。
- 出现改写其他分支文件、修改.csproj/NuGet、同名DTO或伪造测试PASS：STOP。
- 交付ZIP须含文件SHA与完整源码/测试、旧源码引用SHA、测试ID及原始可重放证据；实际不能运行则标明NOT_RUN。

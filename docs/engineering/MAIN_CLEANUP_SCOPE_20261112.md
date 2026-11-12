# AutoTest main 清理记录｜2026-11-12

基准 main：`4530aba0079e8b2344e4ff5e706049450458a45b`。本提交只删除失效的**旧展示入口和 UI 专项**，不声明 R1 已完成。

## 删除范围
- 独立 `AutoTest.DeviceLab` WPF 可执行工程及 Desktop/Solution 引用；**保留** `AutoTest.Devices` 中经过实测的 WinUSB、CAN RX、DeviceLabSession、审计/安全底层与设备测试，避免抹掉现役通信成果。
- 旧手动台架 `Desktop/Bench`（未接正式实体站点），等后续以经过审查的真实台架独立接入。
- `G4ToolsWindow`、`ModuleToolsWindow` 以及依附的 Mock/演示串联页和对应展示层测试；不删除底层协议、资源、生产匹配、报告等领域模块及相应测试。
- 旧 TEST 双轨展示中 `Tools/Testing/LabTestWorkspaceView` 一套独立 Mock 工作区，保留当前真正带工程文档事务的 `Testing/TestEditorView`；移除主窗口 F5 / Run Mock、Mock 运行菜单、RunMonitor 区域。
- 旧 `LabProductionToolView` 是演示 Mock 出厂/扫码界面，因此移除；不将其误认作新串口扫码正式功能。

## 保留与后续
- PROJECT：`V1Workspace`、ProjectRepository、AssetCatalog、ReferenceValidator、文件操作；当前 UI 仍主要是工程树、导入、基础 DBC Factor 编辑。
- DBC：保留解析、编辑、信号解码和独立工具源码。当前独立工具尚未统一绑定 PROJECT 的资源版本 / 运行时 CAN 数据；后续专门审查接入，不在清理 PR 里假装完成。
- TEST：保留 Document/StepEditTransaction、Prepare/Test/Cleanup、结构编辑、持久化与预检。目前旧编辑器只提供 Sequence/If/Loop/Assert/Wait/SetVariable 等部分编辑；缺少正式 Step 类型完整参数编辑、断点/单步物理调试闭环。当前源码保留回归测试使用的 Mock Run 函数（未在 UI 暴露）；后续在真正 Test 执行器集成时再移除或隔离。
- SCAN：main 中 `BarcodeInputRouter`、`KeyboardWedgeCapture`、`SerialBarcodeCapture`、`LabScannerAdmission`、匹配/规则模型是**已有基础**；主 Shell 之前暴露的仅是 G4 Mock 扫码页，不是 PR #41 的新物理扫码入口。扫码后续单独接入，禁止以 Mock 演示冒充实扫放行。
- 现有 `AutoTest.LabLinkGui` 独立 USB 诊断工具未删除；它不等于已删除的 DeviceLab。

## 核对范围
本批清理以文件/项目引用、窗口入口和保留模块边界的源码核对为主；不再额外配置清理专用 CI。不得因删除旧展示层测试后计数减少而宣称产品功能增长，不能引入未经授权的硬件输出。首次提交已有的 Windows 检查结果保留在 PR 历史中，后续不以重复跑 CI 作为清理推进条件。合并仍单独审查 main 的变更范围。

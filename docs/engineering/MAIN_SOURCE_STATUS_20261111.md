# AutoTest｜main 实际代码状态与模块接线基线

> 核对日期：2026-11-11（America/Los_Angeles；GitHub UTC 为 11 月 12 日）  
> **审计源 HEAD：`0344c0d06d393690fac6b941ea02d8c9cd2c4740`**  
> 范围：只核对 GitHub `main` 的实际文件、核心入口及相关已关闭 PR 的交付状态。**未运行 Windows Build、单元测试、WPF、硬件联机或 CI。**  
> 本文是以当前源码为依据的**代码状态快照**，不是正式功能验收和产品交付声明。任何后续源码变化均须重新核对所涉项。

## 1. 主线总体判断

`main` 目前具有真实的后端领域模块和设备诊断工具，**但不具有可操作的 R1 主程序业务 UI**。这不是“功能已基本完成、只差接线”这么简单：既有 UI 适配器被主动清理，`AutoTest.Desktop` 现在明确显示“工作区尚未接入”；且 `PrismRegistration` 只注册 `MainWindow`。

**因此，主线实物用户在打开 Desktop 后，无法从该窗口操作工程/DBC、扫码、USB/CAN、TEST 编辑或自动出厂测试。** 真实物理 USB/CAN 的历史现场观测属于对应旧快照证据，不能自动外推为当前主线完整连接或执行 PASS。

### GitHub 组织事实

- 远端分支（本轮读取）：`main`、`local` 两条；没有独立仍活动的各专业长期开发分支。
- PR #42 已合并：对应清理 V1Workspace、G4 组合器、旧 DeviceLab UI、旧 Mock 工具窗口等，**但保留真实设备底层**。
- PR #41 / #40 / #39 / #38 / #36 是**已关闭、未合并**的历史 UI 候选，不能视为主线功能，也不应整包重贴到 main。
- 同仓库的 `tools/project-control-v2` 是另一个通用管理工具项目，**不是** AutoTest Desktop 的应用代码。
- `SAEF/`、部分 `docs/` 与 `samples/` 仍保留历史阶段档案及模拟样例；这不代表相应阶段实现仍在 Desktop 注册。

## 2. 主线模块代码事实（先看实际文件，再判断生命周期）

| 模块 | main 中的真实实现 | 可从当前主线直接得出的结论 | 尚缺什么 |
| --- | --- | --- | --- |
| Desktop / Shell | `src/AutoTest.Desktop/App.xaml.cs`、`Bootstrap/PrismRegistration.cs`、`Shell/MainWindow.xaml`、主题 | Prism Shell 仅注册 MainWindow；`AutoTest.Desktop.csproj` 无 AutoTest 业务项目引用；窗口文本明确业务未接入 | 完整的正式产品 UI、共享会话、导航、工程视图、测试编辑、工具弹窗与实测流程 |
| PROJECT / 资源 | `AutoTest.Projects/Project/ProjectRepository.cs`、`ProjectDefinition.cs`、`Resources/ResourceEditService.cs` | 已有工程文件保存、打开、资源修订及增删改/撤销领域接口 | 工程树/文件管理 UI；真实磁盘项目打开—编辑—保存—关闭—重开的操作验收；保存入口字段语义核实 |
| DBC | `AutoTest.Projects/Dbc/`、`Dbc/Editing/DbcDocumentStore.cs`、`DbcWorkbenchDocument.cs`、`DbcSignalCodec.cs` | 已有 DBC 文件读写、消息/信号编辑和编码解码领域实现；部分 DBC 指令不支持编辑时采用拒绝策略 | UI 导入/编辑/保存、报文选取/周期发送、与真实共享 CAN 通道及 TEST 资源一致性 |
| TEST 文档与编辑 | `AutoTest.Testing/Authoring/TestDocument.cs`、`StepEditTransaction.cs`、`TestDocumentCodec.cs`、`Preparation/PlanCompiler.cs` | 文档编解码、步骤树编辑、Undo/Redo、编译准备代码实际存在 | 正式可操作树形编辑器、资源引用关联、持久化操作体验、完整控制流语义和集成测试 |
| TEST 运行 | `AutoTest.Testing/Runtime/RunCoordinator.cs` | **此实现仍定义 `MockStation`、`MockRunReport`，其 `StartAsync` 入参为 MockStation**；不能认作真实测试执行器 | 受控实际设备动作适配、真实步骤事实/停止清理/判定、与 TEST 文档唯一模型对接 |
| DEVICE USB/CAN | `AutoTest.Devices/Usb/UsbSessionService.cs`、`WinUsbDiscovery.cs`、`Can/CanFrameHub.cs`、`Station/*` | WinUSB、请求/响应、真实设备识别、CAN 订阅/传输接口及 Station 底层存在 | Desktop 单一 Session/连接入口、共享 CAN Hub、TX 授权与所有 UI 使用同一设备事实；当前 HEAD 实机未复验 |
| 独立硬件诊断 | `tools/AutoTest.LabLinkGui/`、`AutoTest.Devices/Diagnostics/*` | 存在独立诊断窗口和底层控制接口；不是 R1 主业务工作台 | 明确其诊断定位、与正式 Desktop 的一致通信边界、后续去留与实测 |
| SCAN | `AutoTest.Specialists/Scan/KeyboardWedgeCapture.cs`、`BarcodeInputRouter.cs`、`ConfigMatchService.cs` | 键盘枪输入采集/路由及身份匹配领域实现存在 | Desktop 焦点/扫码生命周期、真实规则与工程资源绑定、入厂/复测/统计业务端到端验收 |
| UDS | `AutoTest.Specialists/Uds/`、`CanHubIsoTpLink.cs`、`UdsClient.cs` | 协议/ISO-TP 及只读服务相关实现存在；当前主线移除了旧模拟诊断工作区 | 在受控 CAN1 真实设备事务上的单独实测 UI；授权和真正的响应/NRC/超时证据 |
| Flash | `AutoTest.Specialists/Flash/FirmwareImageParser.cs`、`ProgrammingPreflight.cs`、`FlashTransferScheduler.cs` 等 | 镜像解析、配置/预检和传输计划代码存在；此前 Mock Bootloader 和模拟烧录链已清理 | 专用真实烧录 UI、真实 UDS/Bootloader 执行器、配置参与流程与物理权限、安全恢复实测 |
| Wave | `AutoTest.Specialists/Wave/` | 捕获、参考保存/读取、比较及证据编码领域代码存在 | 共享 CAN/DBC 数据联动、真实波形 UI、参考/阈值/丢样与 Verdict 的完整实测闭环 |
| Records | `AutoTest.Records/Storage/RecordDatabase.cs`、`RunRecordWriter.cs`、`Exports/RunReportExporter.cs` | SQLite、运行事实和报告相关代码存在 | 实际 TEST RunId→StepFact→Wave Verdict→扫码→生产结果的源头一致性与最终报告验收 |
| Production | `AutoTest.Application/Production/ProductionWorkflow.cs` 等 | 有生产编排合同 `IProductionStageExecutor` 等领域实现 | 确定唯一真实执行入口、扫码与判定时序、结果落库和正式自动化出厂闭环 |

状态词必须分开：`SOURCE_PRESENT`（文件存在）、`API_REVIEWED`（接口审查过）、`WIRED`（已接入运行入口）、`BUILT`（精确 HEAD 本地编译）、`OPERATED`（真实软件操作）、`PHYSICAL_VERIFIED`（实体设备证据）、`ACCEPTED`（负责人正式验收）。**某一状态不能替另一状态。**

## 3. 三处需要立即正视的源码断层

1. **正式 Desktop 只有壳，没有服务和业务 View。** 不是把原来的旧窗口打开就能用；要按批准的产品 UI 重新建立 Shell/工程文档/导航/工具窗口与应用适配。现阶段不能说“R1 UI 已接线”。
2. **唯一 TEST 文档模型与真实执行仍未连上。** 旧第二套 Lab TEST 已清除是正确的，但目前运行器仍依赖 `MockStation`。不应恢复 Mock 按钮或用 Mock 结果充当真实出厂 Verdict。
3. **领域模块之间有接口，但缺少唯一可用的用户操作链。** 工程/DBC、扫码、设备连接和 TEST 编辑虽各有类，未在同一主程序运行/导航/文件生命周期中形成真实闭环。独立诊断窗口不等于正式 R1 已集成。

## 4. 从这里重新推进——一条可真实操作的最短产品链

**阶段 A｜Desktop 与工程文件生命周期（优先）**

- 建立正式中文 Desktop Shell 的唯一工程上下文和可操作文件树。默认正常启动，可新建/打开/关闭/保存工程；支持资源增删和撤销、DBC 从磁盘导入与打开、编辑、保存、重开。
- 只接 `AutoTest.Projects` 现有领域入口，明确任何需要改领域合同的差异，不复制第二份工程/DBC 数据模型。
- 用户可直接用鼠标完成一次工程和 DBC 文件全生命周期；重复操作、取消与保存冲突不应卡住 UI 线程。
- 完成时再审核这条链的实际类/文件、构建和手工操作证据。

**阶段 B｜连接、扫码与 TEST 编辑（围绕同一工程上下文）**

- Desktop 唯一连接入口接 `WinUsbDiscovery` / `UsbSessionService` / `CanFrameHub`，显示连接、断开、错误码与操作日志；不凭 Mock 观测宣称物理动作 PASS。
- 扫码枪键盘输入从 `KeyboardWedgeCapture` 进入真实输入区域，接工程身份规则和清晰的输入状态，避免焦点丢失。
- TEST 树编辑直接依赖唯一 `TestDocument` / `StepEditTransaction` / `TestDocumentCodec`，以真文件保存和重新打开验证；禁止新增另一套编辑器/TEST 模型。
- 上述三项允许分工独立实施，但主线每次只接已审查且可以实际操作的一段；接口所有权和共同 Station/Project/Run Context 由 Integration 管理。

**阶段 C｜真实专业闭环**

- 先核实 CAN/DBC/接收与真实波形参考及 Verdict；再核对扫码身份匹配和正式出厂判定；将成熟动作接入单一 TEST 执行入口；Records 使用同一运行事实生成持久化报告。
- UDS 与 Flash 保持独立实测和物理操作边界，真实烧录必须另行批准；手动台架同样需基于真实设备控制而非 Mock 重建。

## 5. 分工与主线写入约束

- **PROJECT/DBC**：工程存储、资源和 DBC 文件可编辑性；不拥有正式 Shell。
- **UI/INTEGRATION**：唯一 WPF Shell、导航、文档视图、App 注入及响应速度；只通过模块明确接口接入，不复制领域实现。
- **DEVICE**：唯一设备连接、CAN Hub、实体控制及安全事务边界。
- **SCAN/TEST/WAVE/UDS-FLASH/RECORD**：各自领域内容和专业界面适配，只有经过接口审查才能接进统一运行上下文。
- **项目管理 AI**：每次以目标 `main` 的精确 HEAD、文件、API、当前变更及验收证据为判断依据；不以 PR 数量代替代码状态。由管理 AI 明确每次修改的文件责任和完成条件，Codex 不自主重构或猜测修复范围。
- **Git 规则**：`main` 为唯一对用户可见的代码基线。按小的可审查集成片段直接更新 main；写入前核对当前 HEAD，禁止覆盖并发改动，禁止无关清理、无依据 Gate PASS。
- **验证规则**：不重新开启反复耗时自动 CI。只有在需要具体可运行证据时，安排本地 Codex 单次受控 Build/Smoke/Test；错误给原始日志，项目管理 AI 定位和安排精确修复。
- **本文不批准任何物理写入、烧录、正式出厂放行或覆盖已有用户文件。**

## 6. 下一次最小交付的退出条件

首个业务增量不是“做完全部 UI”，而是：

1. 主界面显示真实项目，能新建、打开、关闭、保存、删除**用户明确选择的工程资源引用**；涉及物理文件删除时需另行确认。
2. 导入真实 DBC 文件，在工程树中打开并编辑，再保存和重新打开；旧引用版本/文件冲突可以解释，不假死。
3. 每项 UI 命令确切落到 `AutoTest.Projects` 一套已有领域服务；主界面与底层数据一致。
4. 提交该增量涉及的文件清单、函数/API 接线图、真实 Windows Build/启动与用户操作反馈。**未验证就标为待实测，不宣称 ACCEPTED。**

后续连接/扫码/TEST 的集成建立在可操作的工程文档生命周期上，不再在空壳里继续堆功能菜单。

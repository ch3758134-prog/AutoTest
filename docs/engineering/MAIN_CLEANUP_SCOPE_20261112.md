# AutoTest 主线源码清理｜2026-11-12

基线：`main` 原始 `4530aba0079e8b2344e4ff5e706049450458a45b`；清理候选详见 PR #42。

## 已删除的历史前端与阶段组合

- 独立 `AutoTest.DeviceLab` WPF 程序，保留 `AutoTest.Devices` 的主控通信底层及原设备测试。
- `Desktop/Bench` 旧台架、`Desktop/G4` 旧演示工具窗口、旧 `LabTestWorkspace` 和 Mock 生产扫码页。
- 旧 `AutoTest.Application/V1/V1Workspace.cs` **直接删除**，没有改名、迁移、兼容包装或复用其实现。
- `AutoTest.Application/G4` 整体删除；旧 V0 Mock 命令路由与专用组合器一并删除。
- 删除原来依赖 V1Workspace 的 Desktop 工程编辑 ViewModel / PROJECT Explorer / TEST Editor / Inspector，以及仅为该链路服务的 G4 波形回放适配器和录制回放适配器。
- 删除依赖上述类型的阶段性 Mock 演示测试、旧 Integration.Tests 项目；同步移除解决方案与后端过滤配置中的引用。

## 当前保留的正式业务领域

- `AutoTest.Projects`：ProjectDefinition / ProjectRepository / 资源版本 / DBC / ControllerConfig。
- `AutoTest.Testing`：TestDocument / StepEditTransaction / PlanCompiler / Runtime 领域代码。
- `AutoTest.Devices`：WinUSB / CAN / 原设备诊断与控制底层；不把已删除的 DeviceLab **界面**等同于驱动源码。
- `AutoTest.Specialists`：Scan / UDS / Flash / Wave 后端领域。
- `AutoTest.Records`：SQLite / 记录 / 报告领域。
- `AutoTest.Application/Production`：生产业务编排，与 V1Workspace / G4 演示链路解耦。
- `AutoTest.Desktop`：最小可启动 Prism Shell；故意不声明任何尚未接线的真实业务功能。剩余专业工具的 View / ViewModel 处于未接线状态，待逐模块决定接入或清理。

## 清理范围与验证

本批属于**源码边界整理和依赖删除**，不包括将 PR #41 的大型前端候选直接合入。根据要求不额外配置 CI，不主动将 CI 通过解释为用户实测；编译、WPF 启动和实物接线仍待本地核验。

后续必须按工程生命周期、DBC、TEST、连接、扫码、手动台架、UDS、Flash、Wave、Records 的实际产品行为，逐模块实现并由人工审查。不得将模拟、回放、真实证据混为一谈。

## 第二批：并行 LAB 与 V0 根目录交付残留（2026-11-12）
- 删除 `AutoTest.Testing/Lab` 第二套文档/编译/执行实现及专属测试。保留权威 `Testing/Authoring`、`Preparation`、`Runtime`。
- 删除 `AutoTest.Specialists/Scan/Lab` 独立模拟扫码生产系统及专属测试。保留 `IdentityRuleSet`、`ConfigMatchService`、`BarcodeInputRouter`、`KeyboardWedgeCapture`、`SerialBarcodeCapture` 与 `Application/Production`。
- `AutoTest.Projects/Dbc/Lab` 的三个实际 DBC 文件编辑/回放类，原样迁至 `Dbc/Editing` 并只变更命名空间；相应测试与未接线 DBC 工具引用同步更新。这不是保留第二套 LAB 测试框架。
- 删除早期 V0 源码交付包遗留的六个仓库根目录文件（Codex 旧执行合同、哈希清单、文件树、回传单、增补路径、初始验证报告）；历史仍可通过 Git 查看。
- 不修改工程文件外部格式、DBC 语义、扫码策略或设备协议。
- 按维护者要求此批不运行 CI；提交信息标记 `[skip ci]`，尚不声称本地 Windows 编译或实体联机已验收。

## 第三批：未接线 UI 和 Mock 烧录链（2026-11-12）
- 移除未注册到最小 Shell 的整组 `Desktop/Tools`、`Desktop/Production`、`Desktop/Workbench` 与对应旧 UI 测试；保留主程序、Prism Shell、主题、以及针对独立真实 LabLinkGui 的诊断测试。
- 移除 Flash 的 `MockBootloader`、`MockProgrammingWorkflow`、`ProgrammingJobCoordinator`（只接受 V0 Mock 授权）、`ProgrammingStageHandlers`（Mock 特定请求）和 `FlashDryRunWorkspace`，以及模拟烧录专属测试。
- 将原测试文件中实际可复用的 HEX/S19/BIN 镜像解析测试提取为 `FirmwareImageParserTests`；保留镜像解析、通用刷写计划/预检/配置/块传输领域源文件。真实刷写执行器尚未接入。
- 去掉旧 `UdsDiagnosticWorkspace` 模拟 ECU，保留 UDS 服务文档类型/文件读写及 Replay。通用 `UdsClient` 现对所有非只读服务拒绝，直到物理授权链单独集成；改用受控测试通道验证只读响应、NRC 及超时。
- Desktop 当前是干净的最小 WPF Prism Shell，不包含任何已接线的工程/DBC/自动测试界面；后续按正式产品职责重新构建。
- 不主动运行 CI，未做 Windows 本地编译/实机测试；审核以本次源码差异和依赖关系为依据。

## 第四批：诊断设备正式命名（2026-11-12）
- 最后一个 `DeviceLabSession.cs` 不属于已移除的 WPF DeviceLab 程序，它是只读主控 USB/CAN 诊断边界。保留真实设备实现，但将类型、文件和测试统一更名为 `DeviceDiagnosticSession`、`HardwareDiagnosticBackend`、`IDeviceDiagnosticBackend`、`DeviceDiagnosticReadOnlyCommands` 等；默认日志目录使用 `DeviceDiagnostics`。
- 没有改变 USB 命令白名单、B0 授权、主控收发协议、安全行为或公共实体测试来源。无新 CI，真实设备联机结果不在本提交声明。

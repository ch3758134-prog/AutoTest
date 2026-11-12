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

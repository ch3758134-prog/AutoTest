# AutoTest

离线 Windows ECU 集成测试与开发上位机，采用 .NET 10、WPF、Prism。当前仓库以 **真实设备能力、工程数据模型、测试文档模型、诊断与记录领域层** 为研发基础。

## 主线边界

- `AutoTest.Core` / `Contracts`：领域基础类型及跨模块契约。
- `AutoTest.Devices`：WinUSB、主控板协议、CAN 通道与站点控制底层。
- `AutoTest.Projects`：工程定义、资产资源、DBC、控制器配置、Revision 及引用检查。
- `AutoTest.Testing`：TEST 文档、编辑事务、编译准备、执行领域逻辑。
- `AutoTest.Specialists`：扫码、UDS、烧录、波形领域能力。
- `AutoTest.Records`：执行事实、SQLite 记录、统计及报告。
- `AutoTest.Application`：生产业务编排。旧 G4/V1 组合器及 V0 模拟网关已经退出主线候选。
- `AutoTest.Desktop`：最小 WPF 启动壳（旧未接线的 Tools UI 已退出）；现阶段**不提供已接线的工程编辑、自动测试或设备控制**。不得将启动成功误认为业务闭环通过。

## 当前状态

当前为 **主线整理候选**，正式前端和物理会话尚需按模块接入。后端的 API、测试工程与物理硬件通过情况应逐项确认；演示/Mock 结果不能作为实体 ECU 或出厂判定证据。当前实际源码状态、真实入口、模块责任以及首个 PROJECT/DBC 主界面接线退出条件，详见 [`docs/engineering/MAIN_SOURCE_STATUS_20261111.md`](docs/engineering/MAIN_SOURCE_STATUS_20261111.md)；历史清理决策详见 `docs/engineering/MAIN_CLEANUP_SCOPE_20261112.md`。

本仓库旧阶段文件可通过 Git 历史检索，不再将历史交付包说明作为本次产品的正式 README。

## 仓库分支入口（2026-11-13）

本仓库使用分支隔离不同工程，`main` 只作为 **Windows HOST 的正式集成主线**：

- [`Main_Control`](https://github.com/ch3758134-prog/AutoTest/tree/Main_Control)：主控板固件，`Project/Template.uvprojx`。
- [`Key_Sim`](https://github.com/ch3758134-prog/AutoTest/tree/Key_Sim)：按键板固件，`WS/EPB_EOL/`。
- [`Project_Management`](https://github.com/ch3758134-prog/AutoTest/tree/Project_Management)：独立项目管理系统、工程决策与受控档案；首次入场从该分支 [`tools/project-control-v2/START_HERE.md`](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/tools/project-control-v2/START_HERE.md) 阅读。

**过渡说明**：主线中原有 `tools/project-control-v2/`、`SAEF/` 与部分管理文档暂不物理删除，以免在跨分支启动和审批流程实测前破坏已有入口。管理工具后续研发应在 `Project_Management` 进行；此处的旧副本不是第二个独立的管理权威。原理图由管理档案保管，可编辑固件源码以各固件分支为准。任何管理分支的发布或 Gate 变更仍须独立审批。

# AutoTest

离线 Windows ECU 集成测试与开发上位机，采用 .NET 10、WPF、Prism。当前仓库以 **真实设备能力、工程数据模型、测试文档模型、诊断与记录领域层** 为研发基础。

**开始研发或 UI 设计前先阅读：** [现行业务与产品职责决策](docs/project-management/new-baseline-20261112/PM-BUSINESS-DECISIONS-20261114.md)（手动台架功能按键、取消独立 CAN 工作台、扫码生产自动运行等）和 [项目管理入口](docs/project-management/README.md)。

## 唯一 UI 设计源（G3-UX）

UI/UX 专项在专用分支 [`ui/ide-design`](https://github.com/ch3758134-prog/AutoTest/tree/ui/ide-design) 持续更新**唯一现行交互 HTML** `docs/ui/AutoTest_UI.html`。**当前 HTML 尚待 UI 专项首次上传；G3-UX v3 未获验收，不是现行 WPF 实施基线。**用户负责实际交互/视觉验收，项目管理在 UI 候选交付后审查产品、硬件与后端接口一致性。已接受版本才准进入 `main`，并按精确 commit SHA 指导后续 WPF UI 实施。详见 [UI 设计源与交接规则](docs/ui/README.md) 和 [Issue #51](https://github.com/ch3758134-prog/AutoTest/issues/51)。旧 `docs/ui-r1/reference/` 属历史参考，不与现行设计争夺权威。

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

**当前有效产品决策入口：** [`docs/project-management/README.md`](docs/project-management/README.md)。2026-11-14 按项目负责人要求清除 `main` 的历史 `SAEF/` 目录；其中旧 Gate/工作产品不再作为当前产品批准依据。历史存档仍可从 Git 历史及 `Project_Management` 分支查找；工程原始资产应按实际来源与版本辨别。`main` 的新 P0～G3 批准文件保留。

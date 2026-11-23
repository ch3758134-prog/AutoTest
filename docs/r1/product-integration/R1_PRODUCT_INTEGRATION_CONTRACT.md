# AutoTest R1｜正式产品集成合同、当前实现与准出门槛

> 分支：`R1`。`main` 受控，不接受功能性开发直接写入。  
> 当前状态：**产品化集成进行中 / Windows 构建未验证 / 无现场 ECU 验收 / PRODUCTION NO-GO**。  
> 此文件不是额外的工程配置源；真实配置只有 ECU 工程资源及工位部署指针。

## 1. 产品主线及职责冻结

AutoTest 是 **通用 ECU 真实自动测试集成环境**，HydEPB 为首个真实样本，单 PC、单工位、单 DUT，无 Mock 正式执行。

- **PROJECT**：正式 ECU 型号、唯一 TEST 项目、测试用例/公共流程、DBC、波形、固件、安全算法、原生 Flash 发布以及当前型号的夹具准备/收尾工艺。
- **STATION**：真实 WinUSB 主控、ECU CAN1/KEY CAN0、IGN、夹具 1/2、USB COM 扫码枪、未来真实实体 START 输入。COM 和设备身份不复制进各个 ECU 工程。
- **EXECUTION**：一个真 TEST 引擎和原生 Flash 引擎。开发调试可以单步、单用例或选中范围执行；正式生产只能运行工位部署版本的完整已启用 TEST 项目。不得建立第二套 TEST 定义。
- **RECORD**：DUT 身份、每次 Inspection/FlashAttempt/CaseRun、StepOccurrence、CAN/波形证据、报告和复检分别有身份。扫码次数不等于新增 DUT；FLASH USB ACK 不等于烧录成功，GPIO 电平不等于夹具机械到位。

统一正式生产过程：**真实扫码 → 正式型号工程 → 工位/夹具准备 → 软件或实体 START（统一一次性仲裁）→ 必须执行当前原生前置 FLASH → ECU APP 后验 → 按工程顺序执行 TEST → 真实 RECORD → 受控收尾**。用户不需要每台 ECU 重选用例、重导入 XML、重新填写 COM 或重复配置型号。

## 2. 本轮真实落地的架构

1. `StationDeploymentStore` 提供正式的 **工位部署**；工程师从统一「检测准备 → 运行前检查」显式部署当前 ECU 工程。部署校验并冻结工程 Manifest、唯一正式 TEST、原生 Flash Profile/Job、唯一工位准备/收尾工艺的 ID 与 SHA。对生产端，编辑草稿绝不自动生效；当同一路径的已部署内容发生变更，采用严格拒绝重新部署，不回退旧 `automatic-test-plan.json`。
2. `AutomaticTestPlanService` 变为已部署工程的只读投影；操作员端不再提供另一套 TEST 方案选择。新旧操作流程不能混用、不能按文件显示名称猜工程。
3. 真实扫码枪每条完整输入独立发出 `CompleteBarcodeReceived` 事件，包括重复编号；生产投影使用明确条码规则、正式工位型号登记、已部署工程与 `ProductionPreparationService` 交叉验证。重复扫描只更新观察，不创建新 DUT/Run，也不触发夹具、FLASH 或 TEST。
4. `FrozenProductionPreparation` 固定 TEST 用例顺序、固件/安全算法/原生 Flash Job，**新增工位准备/收尾动作资源及其 SHA**；生产配置指纹包含夹具工艺版本。只有核对真实快照才能进入后续阶段。
5. `ProductionPreflight` 拒绝正式实体运行的 `RequiresFlash=false`。生产执行层再次做防御式拒绝。FLASH 不能使用 `MockObservation` 事实冒充成功；要求真正的实体阶段证据及持久化结果，否则不能进入 TEST。模拟/证据缺失/不持久化不能成为正式 PASS。
6. 原生烧录与 TEST 核心引擎保留；旧式扫码 Recipe、FlashSettings 不再作为新工程主要入口。独立扫码枪、原生 Flash、手动台架、DBC/波形工具仍能独立使用。

**说明：** 工位部署目前是“源工程路径 + 指纹冻结”而不是独立拷贝的完整不可变发行包。工程师继续修改这个路径会导致生产配置失效并要求重新部署；这比静默采用草稿安全，但正式产品仍需要完善不可变离线发行包与部署回滚。

## 3. 必须继续消除的架构阻断（不得宣称已完成）

| 编号 | 阻断项 | 严格完成条件 |
|---|---|---|
| PI-01 | 唯一正式生产执行入口 | `AutomaticTestHomeViewModel.RequestSoftwareStart` 不再是空实现，真正接 `ProductionCommandHandler`，实体 START 共用 `ProductionStartGate`；无法进入正式运行时静默拒绝，不用置灰或假 RUN |
| PI-02 | 真实夹具工艺执行 | 根据已冻结的 `InspectionStationProcedureDocument` 调用统一 `HardwareConnectionService`，必须确认独占、失败不伪造机械到位、受控恢复 |
| PI-03 | 前置原生 FLASH | 只执行工程已发布冻结 `CanToolFlashJob`，真实 WinUSB/CAN/UDS，APP 后验与真实证据归档，禁止任何模拟和自动擦写重试 |
| PI-04 | 正式 TEST 顺序执行 | 调用唯一 `RealTestWorkbenchService` / TEST 后端，用正式的全范围 TEST 项目和冻结修订，不把开发 RunId/Report 冒充生产 CaseRun |
| PI-05 | RECORD 合法生产结案 | `ProductionResultCoordinator` 与真实 RecordDatabase 的终态提交/报告闭包整合；不能把普通 `IRunFactSink.CommitAsync` 的 Unknown 结果当作生产 PASS |
| PI-06 | 工位部署发行包 | 固定独立不可变发行版与资源指纹，支持工程师修改草稿而不影响现场执行，提供明确部署/撤销/恢复 |
| PI-07 | HydEPB 历史用例完全迁移 | 逐一回读原始实测文件并修复 8 个真实用例，禁止猜造 DBC 信号、屏蔽未知判据或跳过跨用例周期 CAN 准备 |
| PI-08 | 全局一致的操作界面 | 正式与开发两种操作入口共享单一状态/工程资源事实；错误可定位，不向操作员展示 GUID/旧配方；工具窗口生命周期一致 |

## 4. 自主验收（不能转嫁给用户）

**源代码与资源合同**：Windows `dotnet build HOST/AutoTest.sln`；跑项目各对应 `dotnet test`（必须在 Windows，当前容器无 .NET SDK）；加载旧工程、编辑、保存、退出、重开；唯一资源引用、重复 StepId、版本、波形及 Flash Job 完整闭包；验证只读 WPF TextBox 不作 TwoWay。

**软件全路径（无实体硬件）**：无项目、无工位部署、已部署资源变化、未发布 FLASH、缺失夹具工艺、格式错误扫码、重复扫码、型号冲突、设备断连、双击 START、切换模式、应用关闭窗口。不得出现静默假 PASS、程序异常、旧工程突变或跨工程资源泄漏。

**实体硬件**：扫码枪首次真实试扫并绑定、自动恢复同一 USB 实体；主控 ECU CAN 波特率设置回读；夹具 1/2/IGN 实体命令与 GPIO 证据、机械反馈真实能力确认；单项原生 Flash 成功/失败日志；TEST 真实 CAN/KEY_SIM/DBC/波形证据；完整 Inspection 一次执行和复检不重计；断电、拔 USB、取消烧录及测试失败后的 UNKNOWN/ABORTED 恢复；全量报告和记录复核。

**准出定义**：
- `SOURCE`：仅代码写入完成。
- `STATIC`：静态合同/结构审查通过。
- `WINDOWS`：Windows build + WPF 启动/操作/保存恢复通过。
- `PHYSICAL`：真实设备证据通过。
- `PRODUCT GO`：上述全部满足且 PI-01 至 PI-08 已关闭。

本轮 **仅 SOURCE + 受限静态检查；未 WINDOWS、未 PHYSICAL、非 PRODUCT GO**。不得用开发模拟、单个功能的成功截图或代码指纹替代完整产品准出。

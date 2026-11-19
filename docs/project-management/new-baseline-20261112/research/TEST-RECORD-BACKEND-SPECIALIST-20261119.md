# AutoTest｜TEST-RECORD 后端专项设立与真实执行结果报告门禁（2026-11-19）

## 一、负责人决定与目标

负责人明确要求：**现在派独立专业 AI 研究并补齐真实 TEST 执行中的步骤结果、执行日志、测试报告、证据及与测试用例编辑器的关联**。不希望等完整 UI 做好才在实际 ECU 测试中发现后端不足而被阻断。

用户此次关注的是 NI TestStand / ecu.test 式的“编辑用例时明确判定、测量、记录与报告行为，执行后能产生完整步骤事实、日志、报告”的**后端执行结果业务能力**，**不是**普通 UI 多选/拖放/自动保存。

**状态**：`BACKEND_SPECIALIST_ASSIGNED / BRANCH_CREATED / FORMAL_ISSUE_CREATED / RESEARCH_PENDING / FIELD_PENDING`。此记录不代表研究完成或后端已补齐。

- 开发分支：`feature/test-record-backend-20261119`，始于 `HOST@ffb23f8c05ec4a64b82cd781db6357685d082c04`。源代码位于仓库根 `HOST/` 平级工程目录。
- GitHub 任务：[Issue #64](https://github.com/ch3758134-prog/AutoTest/issues/64)。
- 分支入口：[docs/test-record/START_HERE.md](https://github.com/ch3758134-prog/AutoTest/blob/feature/test-record-backend-20261119/docs/test-record/START_HERE.md)。
- `main` 仍是全系统固定基线，不直接实施试验性后端改造。

## 二、现状代码审查摘要（静态结论）

| 现有代码 | 已存在 | 阶段性缺口 |
|---|---|---|
| TEST `RealRunCoordinator` | 真实预检、Write/Read/WaitUntil/AssertSignal 与 Verdict，运行结束返回 `RealStepFact` | StepId 不足以标记循环/调用多次执行；缺独立 Occurrence、开始/结束/耗时及统一事件流 |
| Desktop `RealTestWorkbenchService` | 使用 TEST 和 `RealRunEvidenceRecorder`，用户确认后在真实工位运行 | 未见贯通 RunRecord/RunStepFact/RunReportExporter 的生产级真实 TEST 完整路径 |
| Desktop `RealRunEvidenceRecorder` | 捕获 CAN_RX / DBC_SIGNAL / DEVICE 事件及溢出计数，最终产生单个 JSON | 内存缓冲 + 运行结束写入，不等于实时耐中断的事实日志 |
| `AutoTest.Records` | RunRecord、RunFact、RunStepFact、存储、证据完整性、HTML/JSON/CSV 报告 | 与上述真实 TEST 入口的运行结果事实源尚未统一 |
| 现有测试编辑器 | 编写真实执行步骤、条件与期待值、映射资源 | 缺明确的额外测量/结果记录与报告呈现配置后端合同 |

这些是静态代码读回，不表示 BUILD 或实机测试通过。

## 三、目标能力边界

**正式结果合同**应覆盖：`RunId + CaseAttemptId + StepOccurrenceId + StepId + Phase + LoopIteration + FlowCallPath`，真实开始/结束时间、耗时、状态、Expected/Actual/Unit/Quality、错误与证据 IDs；不可把 StepId 直接用作一次运行唯一 ID。

**记录层**应按追加式事实流持久化，区分强制审计事实与用户可配置的额外记录项。正常、FAIL、ERROR、ABORT、UNKNOWN、断连及写入未完成必须可追踪；证据缺失应标不完整，不得伪 PASS。

**编辑合同**由 TEST/RECORD 专业提供字段及业务语义，用户在 TEST 步骤属性里可配置判定、额外测量/变量、日志与报告显示策略；这不允许关掉安全审计、原始设备操作必要事实和强制失败证据。具体 Inspector 布局由 UI/UX 专项决定。

**正式报告**必须直接消费同一结构化事实库及冻结运行资产，包含具体步骤尝试、实测结果、时间与失败原因、设备/DBC/方案指纹、证据附件和完整性检查；不把设备 ACK 当 DUT PASS。

## 四、顺序：实测优先而非大规模重构

- TR-00 **现状调查与数据合同设计**：必须对照成熟 TestStand / ecu.test 的结果生命周期，分析 AutoTest 的具体需求，不照搬无关企业级功能。形成准确源码差异与跨专业 API 清单并报项目管理审查。
- TR-01 **真实可运行 P0 闭环**：先以一个 DBC 信号判断用例完成“编辑记录设置→冻结执行→逐步骤采集→实时/耐中断持久化→HTML/JSON 报告→历史回读”。复用现有 TEST/RECORD，交用户实机观察。不要等待 WAVE/SCAN 的完整集成。
- TR-02 **复杂步骤 P0**：循环多次、准备/测试/清理、短路、暂停、取消、失败、超时、断连时的 occurrence 与报告一致性；关键错误/UNKNOWN 必须留下事实。
- TR-03 **扩展 P1**：公共流程、项目多用例汇总、Wave/UDS/FLASH 附件与报告的正式整合，按已有专业合同审查后分期接入。

每阶段必须分清源码提交、Windows 编译与测试、实体 ECU 结果和正式 HOST 集成；不能用一个绿灯替代其它门禁。

## 五、职责与集成约束

- **TEST-RECORD 专项 AI**：只在 `feature/test-record-backend-20261119` 定点修改 TEST/RECORD 私有后端和专用测试，设计真实编辑/记录 API、正式事实流和报告合同；不负责 UI 美术或主控固件。
- **UI/UX**：`ui/ide-design` 负责 Fluent Light 下真实用户如何配置测试记录、如何查看执行状态和报告。不得另建一套报告结果业务逻辑。
- **PROJECT**：参与测试资源 Revision/SHA、报告模板/配置资源和运行资产冻结的增量接口确认；不改 TEST 运行事实含义。
- **DEVICE/专项工具**：提供已有时间/质量/物理证据接口；不改变 ECU/USB/CAN/KEY 语义。
- **HOST/INTEGRATION**：专项后端完成 Windows 构建、相关自动化测试和用户真机验收后，按照精确版本的受控 PR 定点接入 HOST；未经批准不直接合并 `HOST` 或 `main`。

**优先级**：这是全新 UI 与正式自动测试联调的**前置后端风险消除工作包**，但不阻断 UI 视觉与交互研究，也不要求等所有 WAVE/SCAN/UDS/FLASH 模块完成后才验证基本 TEST 报告链。

## 六、可核查交付

- 已创建 Issue #64，专业分支已建立，启动说明已提交；
- 尚未提交新 TEST/RECORD 实现、Windows 结果或真实 ECU 日志；
- 项目管理对 **TR-00 调查和技术合同** 有审查权，对 shared Contracts/PROJECT/DEVICE 跨专业 API 有边界审查权，对实机能力不得以静态代码声明 PASS。

# AutoTest｜TEST-RECORD 后端专项：先读我

**职责**：TEST/RECORD 后端研究与开发，专门把 AutoTest 自动测试用例的**步骤判定、真实测量、执行事实、日志、报告**接成可验证的完整链路，不是普通 TEST 编辑器拖放 UI，也不重画 Fluent WPF。  
**GitHub 工作单**：[Issue #64](https://github.com/ch3758134-prog/AutoTest/issues/64)。该 Issue 的验收矩阵和职责边界属于正式任务合同。  
**专项分支**：`feature/test-record-backend-20261119`；起点 `HOST@ffb23f8c05ec4a64b82cd781db6357685d082c04`。main 为固定系统基线，不在 main 写开发代码。  
**系统目录**：仓库根有 `HOST/`、`Main_Control/`、`Key_Sim/` 三个平级工程；仅 HOST 代码是本专项默认修改对象。  
**管理状态**：`SPECIALIST_BRANCH_CREATED / ISSUE_ASSIGNED / RESEARCH_PENDING / CODE_NOT_IMPLEMENTED / FIELD_NOT_VERIFIED`。

## 核查起点

必须以实际当前 HEAD 重新回读，不得仅复述本节：

- `HOST/src/AutoTest.Testing/Runtime/RealRunCoordinator.cs`：真实 TEST 解释器，步骤结束时创建 `RealStepFact(StepId, Type, Status, Expected, Actual, Detail, At, Evidence)`。
- `HOST/src/AutoTest.Desktop/Testing/RealTestWorkbenchService.cs`：真实测试入口使用 `RealRunCoordinator` + `RealRunEvidenceRecorder`；现有真实测试运行后保存独立 JSON，未见接入 `RunRecordWriter`、`RunReportExporter` 全链。
- `HOST/src/AutoTest.Desktop/Testing/RealRunEvidenceRecorder.cs`：CAN/DBC/DEVICE 事件先存内存，最终形成 `test-records/{RunId}.json`，需审查掉电/中断和溢出处理。
- `HOST/src/AutoTest.Records/Model/RunRecord.cs`、`RunFact.cs`、`Storage/RunRecordWriter.cs`、`Exports/RunReportExporter.cs`：现有不可变事实、数据库和 HTML/JSON/CSV 报告后端，必须复用。
- `HOST/src/AutoTest.Testing/Authoring/` 与 PROJECT 资源：步骤判定已存在；需给 UI 一个真正可配置的步骤结果/测量/证据/报告接口。不要重新定义第二套 TEST DSL。
- `HOST/src/AutoTest.Application/Production/`：生产结果另有持久化门禁，真实自动 TEST 与生产统计/扫码两者不得混淆。

## 交付分期

**TR-00 调查/设计**：梳理 NI TestStand 的 Step Results / Additional Results / Report Processing，ecu.test 的 Test Package / Signal Recording / Trace Analysis / Report 的目的、操作和数据流。结合本仓库列准确差距。提交结果流架构、数据身份、事件/测量/证据 Schema、报告内容和 UI 访问合同，供项目管理定点评审。

**TR-01 最小真实闭环（P0）**：针对真实 CAN/DBC 判定 TEST，完成“可编辑结果/记录设置 → 编译冻结 → 执行 → StepOccurrence + 实测与质量 + 原始证据绑定 → 持续追加写入 RECORD → 自动生成可打开 HTML/JSON 报告”。标准审计始终保留；用户只决定额外采集及报告呈现。不可另建数据库。

**TR-02 复杂运行（P0）**：同 StepId 循环多次 / 条件分支 / 失败继续或中止 / 用户停止 / 超时 / 断连 / 清理时，准确区分每次 occurrence 以及各证据的时间范围，保证失败与异常不丢最后可持久化记录。以身份和时间戳关联 CAN/DBC/DEVICE 日志。检查事件容量上限与 Dropped/Incomplete 显示。

**TR-03 扩展兼容（P1）**：公共流程调用的多层身份、项目内多用例尝试和汇总、Wave/UDS/Flash 附件挂接合同与报告视图按专业状态补齐；不要等待所有独立专业全部完成才完成 P0。

## 不允许做的事

1. 不重新造 TEST 引擎、DBC 解析器、Record DB、Report 事实源；禁止 Mock 成功替代真机实测。
2. 不自己重画 UI 或在 WPF 私有区域塞入一套结果规则；将 UI 需要的选择项、字段和值返回正式接口合同，由 `ui/ide-design` 研究呈现。
3. 不改变 USB/CAN/IO、主控固件、KEY_SIM、UDS/FLASH 业务状态机，不新增未经验证的硬件动作。
4. 不将设备 ACK 判为 ECU PASS；缺失有效观测/丢失证据必须产生明确 UNKNOWN/INCONCLUSIVE/ERROR。
5. 对 PROJECT、共享 Contracts、DEVICE 跨专业 API 的新增先提出准确的增量接口和兼容性审查，保持代码责任边界。
6. 不直接修改 `HOST` 或 `main`，只在本分支提交；Windows 编译/测试由本地 Codex 做，实体 ECU 验收由用户现场执行。

## 验收证据与交接

分别提交：代码差异/精确 SHA、设计与例程、Windows Build/Test 真实日志、**真实 ECU 用例**的 RunId、StepId/Occurrence、实际值/时间/质量、关联 CAN 帧/DBC 修订、异常终态、DB 记录、JSON/HTML 报告及附件完整性；缺什么明确标记 `NOT_VERIFIED`。按 #64 完成独立验收后才申请接入 HOST 的受控集成。

**交付优先级**：先交一个能实测的端到端报告闭环，不要把大量高级报告样式或 UI 控件研究放在真实结果采集之前。

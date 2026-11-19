# AutoTest｜TR-00 TEST-RECORD 后端源码调查、差距与 P0 设计审查稿

- 日期：2026-11-19
- 专项：TEST-RECORD 后端研究与开发 AI
- 审查基线：`feature/test-record-backend-20261119@2a65aff8479830baab887d779db8722728c72eb8`
- 管理依据：`Project_Management@73f4e172fbd5fc4e2d78585a872dd09c5d1a9593` 的 `docs/project-management/new-baseline-20261112/research/TEST-RECORD-BACKEND-SPECIALIST-20261119.md`、Issue #64
- 状态：**TR-00 SUBMITTED_FOR_PM_REVIEW / SOURCE_AUDITED / CODE_NOT_IMPLEMENTED / WINDOWS_NOT_VERIFIED / ECU_NOT_VERIFIED**
- 修改边界：仅本专项分支；不修改 `main`、`HOST` 分支、固件或物理协议；共享 Contracts / PROJECT / DEVICE 与 Desktop 接线等待责任审查。

## 1. 外部成熟机制：目的、使用方法、数据流

### NI TestStand

NI 官方《TestStand Report Generation and Customization》明确区分：引擎在步骤执行时收集标准 `Step.Result`、错误信息与可选 Additional Results，形成 ResultList；报告和数据库是其后的**结果处理**，并不负责重新判定 ECU。Additional Results 可从步骤属性或自定义数据选择并附条件；Report Options 可选择格式、过滤和报告视图；on-the-fly 可边执行边更新报告。Process Model 负责 UUT 识别、前后置操作、结果日志及报告。

AutoTest 应学“**先产生唯一真实结果事实，再由不同读者处理**”，不照搬 TestStand 允许彻底关闭结果收集的选项：本项目安全/追溯审计必须常开。不要照搬多插槽/批量执行或所有报告格式。

来源：
- https://www.ni.com/en/support/documentation/supplemental/08/teststand-report-generation-and-customization.html
- https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html
- https://www.ni.com/docs/en-US/bundle/teststand/page/supporting-post-failure-information-recovery.html

### ecu.test

ecu.test 2026.x 官方帮助把 **Test case/Properties/Signal recordings/Trace analysis/Test report** 放在同一测试包编辑工作流；运行时执行测试、记录信号、执行可选 Trace 分析、创建报告。Signal recordings 在编辑时选取信号与起止范围；Trace analysis 处理时间相关条件（响应时间、保持范围等），报告分列测试步骤、录波、分析结果、工况配置、附件，单个步骤可看到变量值、期待值与执行调用层级。测试执行窗口有活动步骤、调用栈和实时日志。

AutoTest 应学“**测试时刺激/即时判定 + 有身份的信号采集 + 后置分析与报告追踪分层**”。P0 只需要单条实际 CAN/DBC 判定的额外测量与证据窗口，不强依赖全部 WAVE/UDS/FLASH 实现。

来源：
- https://docs.tracetronic.com/help/ecu.test/en/Main-program/Editor/Test-case-editor/Work-window.html
- https://docs.tracetronic.com/help/ecu.test/2026.3/en/Tutorial/Fundamentals/Lesson-5-Signalrecording-Traceanalysis.html
- https://docs.tracetronic.com/help/ecu.test/en/Main-program/Reportviewer.html
- https://docs.tracetronic.com/help/ecu.test/en/Test-execution/Test-execution.html

## 2. 源码现状：已实现 / 未接线 / 能力缺失

| 领域与精确代码 | 已实现并经静态回读 | 未接线或缺失 | 本轮证明级别 |
| --- | --- | --- | --- |
| TEST `Runtime/RealRunCoordinator.cs` | 单工位真实解释器；Prepare/Test/Cleanup；Read、WaitUntil、AssertSignal、Expect、Write、Loop、If、Wait、Wave、判断 Verdict；缺数据不允许空字符串假阳性；失败尝试清理 | `RealStepFact` 仅 StepId/Type/Status/Expected/Actual/Detail/At/Evidence 字符串；没有开始/结束、持续时间、OccurrenceId、Phase、循环路径、独立质量；当前 `Fact()` 仅追加内存数组，异常常记录为 StepId=default 的 Run/Cleanup；同 StepId 多次在 UI 关联上有歧义 | 源码已核实；实机未验证 |
| TEST `Authoring/TestDocument.cs`、`TestDocumentCodec.cs`、`Preparation/PlanCompiler.cs` | 唯一 TEST JSON DSL，StepNode Properties；映射资源；编译时冻结 JSON + SHA256；`StepTreeValidator` 可预检 | 无结构化录制/附加结果/报告字段语义与验证；仅基础 target/expected/timeout 等属性 | 源码已核实 |
| DBC `Desktop/Hardware/LiveDbcSignalService.cs` | 通过真实 CAN 帧产生 `SignalObservation`，包含 SignalRef、数据、Unit、TimePoint、Quality、EvidenceRef、HasGap；提供 ReadAsync/ObserveAsync 和 SignalReceived | 现有 TEST Result 不保存这些完整字段；SignalReceived 订阅路径和 bounded channel 有丢样/断连语义，Record 没统一消费 | 源码已核实 |
| 真实入口 `Desktop/Testing/RealTestWorkbenchService.cs` | 检查真实 USB、冻结计划和 DBC 修订；真实授权与动作；调用 RealRunCoordinator；最终保存证据 JSON | 未创建 `RunRecord`、未调用 `RunRecordWriter.BeginAsync/AppendStepAsync/CompleteAsync` 和 `RunReportExporter`；预执行/保存抛错无正式可回读终态；RunId 当前由执行器内部创建，不便提前 Begin | 源码已核实 |
| 证据 `Desktop/Testing/RealRunEvidenceRecorder.cs` | 捕获 CAN_RX/DBC_SIGNAL/DEVICE；100000 条内存上限，记录 DroppedEvents；使用临时文件后移动保证最终文件原子替换 | 运行结束才保存 `test-records/{RunId}.json`；断电之前未保存的数据不可恢复；证据字符串与 ArtifactDescriptor/EvidenceRef 没有强链接；无流式事件持久化 | 源码已核实 |
| RECORD `Model/RunRecord.cs`、`RunFact.cs` | 冻结 RunSnapshot、身份/模式分离、RunFact、RunStepFact(Expected/Actual/Attempt)、UTC/证据质量校验 | 既有 RunStepFact.Attempt 不等于 StepOccurrenceId；没有 Phase、起止时间、Unit、Quality 细分、父调用/循环；Development 也强制要求经规则指纹的非空 DUT Identity，当前 UI 真实入口未显式提供 | 源码已核实 |
| RECORD `Storage/RecordDatabase.cs`、`RunRecordWriter.cs` | SQLite WAL/FULL；Begin、追加不可变 RunFact/RunStepFact、Artifact；EventId 幂等；`RecoverInterruptedRunsAsync` 置 Interrupted/Unknown；有可靠完成 `CompleteAsync(RunCompletion)` | `IRunFactSink.CommitAsync` 默认以 Unknown 完成，不得用于真实 Verdict；run_facts 和 step_facts 分两条队列，需明确相关事件身份、写入先后和统一查询序号；StepPayload 只有基础字段 | 源码已核实 |
| RECORD `Exports/RunReportExporter.cs` | 从 DB 查询并导出 HTML 文本、JSON 文本、步骤 CSV；核对 Artifact SHA256；输出 RunSnapshot 及判定状态 | 尚未接入真实 TEST；未输出正式 HTML/JSON 到工程归档路径；缺 Occurrence、时间区间、条件/循环路径、信号质量和丢失提示；报表生成成功不能证明 ECU 实测成功 | 源码已核实 |
| PROJECT `Projects/Project/ProjectDefinition.cs`、`Desktop/Projects/ProjectWorkspaceService.cs` | 项目 Revision、资产 Revision/SHA、工程清单、测试程序、DBC、TCF/TBC 资源；TEST 映射钉住版本 | 正式运行用 ProjectSnapshot/StationProfile/RunSnapshot 的转接、报告配置归属需责任审查；单工位开发未扫码时身份策略未定 | 源码已核实 |
| 生产 `Application/Production/ProductionWorkflow.cs`、`ProductionResultCoordinator.cs` | 执行阶段及记录接口，Mock 与真实放行边界分离 | 生产结果的 existing sink 并不代表当前真实 TEST 已挂接；生产统计和扫码放行不能从 TEST 一条 PASS 自动推导 | 源码已核实 |
| 编辑器 `Desktop/Testing/TestEditorViewModel.cs` | 可确认并启动真实用例，运行结束把 RealStepFact 显示到列表 | 运行中尚无持久化订阅；编辑时无正式结果/采样/报告配置；当前结果仍用 StepId 关联，循环重复不唯一 | 源码已核实 |

自动化测试存在，但 `RealExecutionTests.cs` 的测试端口是替身；`Records.Tests` 的 RunRecord 夹具使用模拟证据。这只能用于逻辑单测，不能作为真实 ECU 验收。当前未取得任何真实 ECU 的 REPORT/RunId 证据，也没有在此环境运行 Windows Build/Test。

## 3. 建议的统一运行事实合同（待审）

### 3.1 身份

- `RunId`：一趟真实测试执行唯一；必须在 BEGIN RECORD 前创建。不能用工程名替代。
- `CaseAttemptId`：同次 Run 内用例尝试唯一；P0 单用例仍填明确身份，项目多用例调度留 P1。
- `StepId`：TEST 文档中稳定编辑资源 ID；在循环重复时保持不变。
- `StepOccurrenceId`：每次进入节点的实际执行实例唯一 GUID；不可直接用 StepId；生成在步骤开始前。
- `ParentOccurrenceId/CallPath/LoopPath`：P0 至少单层/多层 Loop 真实迭代身份与父子关系；公共流程跨文件调用 P1。
- `Phase`：Prepare / Test / Cleanup / 系统级事件，绝不把系统异常伪造成一个有效的 StepId。
- `RunSnapshot`：原有 Project/Station/Plan/LockedAssets 的真正 Revision/SHA；附测试文档指纹与运行软件身份；对未知 DUT 的开发运行采用明确“不具备生产身份”合同，不伪造扫描身份。

### 3.2 标准必录事实（用户不得关闭）

`RunBegin, StepBegin, Observation, StepVerdict, StepEnd, CleanupBegin/End, RunTerminal, RecordIncomplete` 的语义需有明确事件顺序、UTC、主机单调计时、唯一 EventId、Operation/Occurrence 关联 ID。步骤至少记录：
`StepOccurrenceId, StepId, Phase, Parent/Loop, Name/Type, StartedUtc, EndedUtc, Duration, State, Expected, Actual, Unit, ObservationQuality, Verdict, ErrorCode, EvidenceRefs`。

区分：
- `Completed + Pass` 仅在明确成功断言且数据质量有效时成立。
- `Read/Write/Wait` 的操作完成 **不等于** DUT Pass；ACK 只证明设备确认请求，不证明 ECU 物理动作。
- `NoData/Timeout/Disconnected/LostFrames/DecodeError` 不允许成为 PASS；应按当时逻辑归类 Inconclusive/Unknown 或 Error，并保留原始状态。
- Disabled => Skipped；未进入分支 => NotExecuted（若单独输出执行计划覆盖信息）；用户停止 => Aborted；崩溃后未提交 => Interrupted/Unknown。保留 CLEANUP 的独立失败事实，不覆盖先前失败根因。

### 3.3 可配置附加结果

TEST 提供**类型化的 authoring facade**，序列化继续使用现有 `StepNode.Properties` 的命名空间键（提案 `result.*`）而不建立另一套 DSL：
- 必需的 target/operator/expected/timeout/maxAge 仍由现有步骤属性控制；
- `result.capture`：None / DecisionOnly / Window (额外采样范围)；
- `result.extraVariables`：选择要同时记录的变量名；
- `result.captureOn`：Always / NonPass（仅控制额外采样，不关闭标准审计）；
- `result.requirementId`、`result.reportNote`、`result.showEvidence`：关联报告条款、备注和显示偏好；
- P0 针对 Read/WaitUntil/AssertSignal/Expect；采样数量和时间范围必须设上限、明确 Dropped/Incomplete。
- 每个键要有默认值、类型校验、兼容旧文件及明确版本；`PlanCompiler` 应在硬件操作前拒绝不合法的录制条件。报告隐藏不影响底层 RECORD 完整性。

报告模板的独立工程资源以及项目级聚合属于 PROJECT 共享审查，P0 先使用稳定的自动 HTML/JSON 结构和已冻结的 TEST 文档标题/识别字段。

### 3.4 原始证据与时钟

真实 `SignalObservation` 已提供 `SignalRef / Value / Unit / TimePoint / EvidenceRef / Quality / HasGap`，应原样挂接至每次 StepOccurrence 的观测，而不是仅写 `Evidence.ToString()`。原始 CAN_RX/DBC_SIGNAL/DEVICE 事件拥有同一 RunId 和事件时间窗口。报文丢失、订阅器溢出、文件不完整作为有计数的独立事件/报告标志，不能被过滤规则藏掉。

绝对时间使用 UTC；运行内耗时用同来源单调时钟计量，严禁用两次主机 UTC 相减伪装高精度硬件时延。只有能验证匹配的 EvidenceRef 才链接 ArtifactDescriptor；无可验证证据需明确“缺失/未绑定”，不能编造附件。

## 4. 端到端结果流（批准后实施）

```text
已保存 TEST + 真实 TCF/DBC/工程指纹
  -> PlanCompiler (现有冻结/预检)
  -> 读取真实 ProjectSnapshot / StationProfile / Development identity
  -> 分配 RunId / CaseAttemptId；RunRecordWriter.BeginAsync [在任何 ECU 动作前]
  -> TEST RealRunCoordinator
       -> 每次 StepBegin (Occurrence + Phase + LoopPath) 同步确认耐久
       -> 真实设备调用/信号观测
       -> 测量、质量、证据与判定 (只允许真实端口)
       -> StepEnd + RunStepFact / RunFact 追加后确认耐久
       -> 操作失败/停止/断连时走现有清理
  -> RunCompletion (用 CompleteAsync，不用猜测结论的 CommitAsync)
  -> RunHistoryQuery / RunReportExporter (同一 SQLite 事实)
  -> report.html + report.json (+ step.csv) 实际落盘/可打开
  -> UI 只订阅已确认的结果与查询历史，不独立判断 PASS
```

建议在 TEST 域定义 `IRealRunEventObserver`（不可反向依赖 RECORD），回调采用可等待的 `ValueTask`，以保证步骤开始、步骤结论在推进下一硬件操作前按已批准策略持久化。RECORD 域提供对应的适配器；真正 DB Begin、冻结元数据与工位接线由 HOST/INTEGRATION 审核。不要使用无界内存队列、后台 fire-and-forget 或静默吞掉持久化异常；关键写入失败必须停止有风险的下一硬件步骤并进入清理与 Unknown 路径。

SQLite 现有 run_facts 和 run_step_facts 可复用；优先扩展私有 `StepPayload` 的可选版本字段和明确跨表 CorrelationId，使用旧值缺省读取，不对 legacy 事实凭空造 OccurrenceId。需要验证跨表顺序是否必须增加全局 run sequence；如需 SQLite Schema v2，先交 RECORD 迁移及兼容审查。启动时调用 `RecoverInterruptedRunsAsync` 并以真实历史页面/报告显示。

## 5. P0 精确修改接口与责任边界（评审清单）

| 代码接口 / 文件 | 修改建议 | 责任和门禁 |
| --- | --- | --- |
| `AutoTest.Testing/Runtime/RealRunCoordinator.cs` | 在原执行器增加可等待事件观察口、每次 occurrence/Phase/LoopPath、起止与异常事实；RunId 外部分配的只读重载 | TEST-RECORD 可实施 |
| `AutoTest.Testing/Runtime/TestPorts.cs` | 复用现有 `TestSignalResult.Observation`，不改设备收发；定义接收事实的只读结果类型 | TEST-RECORD 可实施 |
| `AutoTest.Testing/Authoring/*`、`PlanCompiler.cs` | `StepResultRecordingOptions` 类型化读写、额外结果边界检查与冻结；旧 JSON 缺省可用 | TEST-RECORD 可实施 |
| `AutoTest.Records/Model/RunFact.cs`、`Storage/RecordDatabase.cs` | 扩展 `RunStepFact` 承载 occurrence/观察质量/起止，序列化兼容；验证 EventId 幂等及迁移 | TEST-RECORD 可实施，SQLite 版本变更需审查 |
| `AutoTest.Records/Exports/RunReportExporter.cs` | HTML/JSON/CSV 显示同一事实、缺失和证据完整性，不重新判定；导出文件归档接口 | TEST-RECORD 可实施 |
| `AutoTest.Contracts/RecordContracts.cs` / `TestingContracts.cs` | 如需共用新事件类型、读取与订阅接口，先提交字段级 Proposal；优先避免修改共享类型 | **需要管理/共享合同审查** |
| `AutoTest.Projects` + `ProjectWorkspaceService` | 真实 `ProjectSnapshot/StationProfile`、报告配置资源的 Revision/SHA 接口；Development 未扫码身份合同 | **需要 PROJECT/HOST 审查** |
| `AutoTest.Desktop/Testing/RealTestWorkbenchService.cs` 与 DI | 真实入口接入同一 RECORD 持久化，终态后归档报告，避免第二条 JSON 真相源；给 UI 查询订阅接口 | **需要 HOST/INTEGRATION 接线审查** |
| `AutoTest.Desktop/Hardware/LiveDbcSignalService.cs` | 优先只读复用现有 Observable 和时间/证据；跨 DEVICE/DBC 修改不在本专项自行决定 | **需要对应专业审查** |
| `AutoTest.Application/Production/*` | 保持生产扫码/统计与 TEST 结果隔离，不让 Development PASS 进入生产放行 | **本 P0 不改** |

## 6. P0 交付分解与真机验收

**TR-01（先纵向贯通）**：一条真实 DBC Signal 的 AssertSignal/Read/Expect 用例，编辑指定目标/比较/超时/额外记录 → 冻结运行 → 每次 occurrence 和真实 Value/Unit/Quality/EvidenceRef → DB 逐条 durable → HTML/JSON/CSV 可打开且同源。至少包括一条实际 CAN 帧的指纹/信号来源。单用例、Development 模式，禁止假扫码进入 Production。

**TR-02（同一合同再覆盖复杂执行）**：Loop 重复 StepId、If 选支、Prepare gate、Cleanup、Skip、失败继续（遵循现有 TEST 语义）、超时、断连、用户取消与中途崩溃恢复；StepOccurrence 必须不串；没有任何有效数据时不能出现 DUT PASS；录制溢出有显式 dropped/incomplete。

**TR-03（P1，暂缓）**：公共流程嵌套、项目多用例尝试汇总、UDS/FLASH/WAVE 标准附件、图形化录波与报告样式深入定制。

Windows 验证要求：精确 PR HEAD、Restore/Release Build、Testing/Records 专项测试、WPF 启动，各记录命令与原始日志；真机要求四条分路径 **PASS / FAIL / 超时 / 断连**，提供 RunId、每次 OccurrenceId、DBC 版本、实际数据、设备事件、持久化历史及 HTML/JSON 文件。逻辑单测不得充当真机 PASS。

## 7. 请项目管理定点评审的决定

1. 批准以唯一 `StepOccurrenceId` + 稳定 `StepId` 为所有结果/证据的身份边界；批准标准审计必录，额外采样和报告呈现可配置。
2. 审核单用例 Development 无扫码情况下 **真实身份** 的最小来源与不计生产统计规则；不接受虚构 DUT。
3. 明确 RunSnapshot 的真实 Project/StationProfile 冻结转接由 PROJECT/HOST 哪个接口提供，避免专项私自重建 station 配置。
4. 确认 P0 在 TEST/RECORD 私有代码可先实施，Desktop 真正接线必须 HOST/INTEGRATION 审查；如需 Contracts/SQLite v2 按变更单另审。
5. 确认日志丢失/记录失败应标 Incomplete/Unknown 并按既有安全清理，而非继续后续真实硬件刺激。

**结论**：现有 TEST 与 RECORD 有可复用基础，但真实 Test→Record→Report 端到端没有接线，不可认定完整。优先做小而真实的 CAN/DBC 纵向闭环，然后覆盖 Loop、失败与中断；绝不因标准报告美化延误真实 ECU 验收。

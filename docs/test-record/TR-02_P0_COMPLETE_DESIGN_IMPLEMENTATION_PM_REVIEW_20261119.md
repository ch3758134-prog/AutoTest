# AutoTest｜TEST-RECORD 后端 P0 全量技术设计、实施与项目管理审查包

**交付日期**：2026-11-19  
**专项责任**：TEST-RECORD 后端研究与开发 AI（只负责执行结果、事实、证据、日志、报告）  
**工作基线**：`HOST@ffb23f8c05ec4a64b82cd781db6357685d082c04`  
**开发分支**：`feature/test-record-backend-20261119`  
**评审 PR**：[Draft #65](https://github.com/ch3758134-prog/AutoTest/pull/65)；**正式任务**：[Issue #64](https://github.com/ch3758134-prog/AutoTest/issues/64)  
**审查所依据的代码取样提交**：`18fa3ff722892ed2c4f934a503bb335178b8aafe`（本档写入时 HEAD 会向前移动，以 PR 最新 SHA 为准）  
**状态**：`RESEARCH_COMPLETED / PRIVATE_BACKEND_IMPLEMENTATION_SUBMITTED / PM_REVIEW_REQUIRED / WINDOWS_BUILD_NOT_RUN / REAL_ECU_NOT_VERIFIED / DESKTOP_NOT_WIRED`。

> 本报告是后端专项审查交付，**不是**真实 ECU 测试成功报告，**不是** HOST 合入许可。设计与现有静态源码均已核对；当前无 .NET SDK，不能把文件存在、GitHub 回读或括号配对写成编译/运行 PASS。

## 1. 目标和成熟产品研究

### 1.1 NI TestStand — 将 Result Collection 与 Result Processing 分离

NI TestStand 的步骤先产生标准结果集，Additional Results 可以把测量/变量/附加数据纳入结果；Result Processing 负责数据库、HTML/XML 等报告，不应该借报告再判断 ECU。On-the-fly logging 在执行期处理已产生的结果，故障后可以保留已处理的前缀。其“记录结果”与“展示结果过滤”是不同概念。**AutoTest 保留安全审计必录**，不照搬允许用户彻底关闭结果或复杂并行工位的配置。

参考：
- [TestStand Report Generation and Customization](https://www.ni.com/en/support/documentation/supplemental/08/teststand-report-generation-and-customization.html)
- [Enable On-The-Fly Reporting or Logging](https://knowledge.ni.com/KnowledgeArticleDetails?id=kA00Z000000P9LdSAK&l=en-US)
- [Supporting Post-Failure Information Recovery](https://www.ni.com/docs/en-US/bundle/teststand/page/supporting-post-failure-information-recovery.html)
- [Code Module Development Best Practices](https://www.ni.com/en/support/documentation/supplemental/09/teststand-code-module-development-best-practices.html)

### 1.2 ecu.test — 测试步骤、记录组、Trace 分析与报告证据关联

ecu.test 的 Test case 负责刺激、读取和步骤判断；Signal recording 允许从运行中保存完整信号时间段；Trace analysis 对采集数据执行独立条件判断；Report viewer 展示步骤、分析、原始曲线与总结果。不能把一条设备 ACK 等同 DUT 信号断言，也不能仅从报告外观推断记录完整。

参考：
- [ecu.test 2026.3 Signal recording / Trace analysis](https://docs.tracetronic.com/help/ecu.test/2026.3/en/Tutorial/Fundamentals/Lesson-5-Signalrecording-Traceanalysis.html)
- [ecu.test Test execution](https://docs.tracetronic.com/help/ecu.test/en/Test-execution/Test-execution.html)
- [ecu.test Report viewer](https://docs.tracetronic.com/help/ecu.test/en/Main-program/Reportviewer.html)
- [ecu.test Recording control](https://docs.tracetronic.com/help/ecu.test/en/Main-program/Action-window/Test-steps-tab/Recording-control.html)

### 1.3 AutoTest 的适配原则

只有一个真实工位，ECU CAN 与按键控制早有独立真实通信和单个 TEST 解释器；此专项**不改**固件、DBC 解码算法、USB/CAN 会话、UDS/FLASH/WAVE 引擎或 WPF 布局。P0 首先完成一种 CAN/DBC 信号的真实 StepOccurrence→ SQLite→证据校验→HTML/JSON/CSV；扩展到原始逐帧 CAN 连续采集必须在 HOST/DEVICE 接线时实现，不能在后端冒充已接线。

## 2. P0 业务数据合同

### 2.1 身份与冻结边界

| 字段 | 实现/来源 | 规则 |
|---|---|---|
| `RunId` | 外部在 `RunRecord` 中分配；`RealRunCoordinator.StartAsync(plan, runId, sink)` 接收 | 全运行唯一，RECORD Begin 在物理步骤之前 |
| `CaseAttemptId` | `RealTestRecordSession` 唯一 GUID | 同一次真实测试用例尝试唯一；与 RunId/StepId 不混淆 |
| `TestDocumentId` | `PreparedTestPlan` 冻结 TEST 文档 | 与冻结后的名称、报告标题、用例编号一同写入 `TEST_CONTEXT` |
| `StepId` | `StepNode.Id` | 编辑资源稳定；循环重复保持原 ID |
| `StepOccurrenceId` | TEST 执行每个启用/禁用节点时生成 | 同 StepId 循环多次每次不同；父/循环路径可还原 |
| `Phase` | Prepare/Test/Cleanup | 系统级异常不创建虚假 StepId |
| `ParentOccurrenceId` / `ExecutionPath` / `LoopIteration` | 当前 TEST 树执行 | 描述节点嵌套、循环第几次；公共流程跨文件调用未来分步扩展 |
| `ProjectSnapshot` + `StationProfile` + 锁定资产 | 现有 PROJECT/HOST 提供的真实修订 SHA | 不从运行结束后的最新工程读回取代执行时版本 |
| `SessionIdentity` + `RecordRunMode` | HOST/PROJECT 现场身份策略 | Development 不得虚构扫码或进入 Production 统计 |
| `HostBuildIdentity` | HOST 打包构建身份 | 不是从源文件默认常量捏造的版本 |

### 2.2 必录内容与可配置内容

| 结果类型 | 标准强制记录 | 编辑时可配 |
|---|---|---|
| RunBegin / TestContext / RunTerminal | 必录（在原 RunFact 流） | 测试用例报告标题/编号/说明 |
| StepBegin / StepEnd | 必录；含真实 Occurrence 与时间 | 不允许关闭 |
| Expected / Actual / Disposition / FailureCode | 必录（原 RunStepFact） | 判定表达式和超时仍由已有步骤主属性决定 |
| 真实信号 Value/Unit/Status/Quality/Source/DBC 版本 | 有信号观测时必录 | 信号映射由已有 TEST Mapping 选择 |
| 额外变量（`result.extraVariables`） | 非安全必录，可以为空 | 最多 16 个，采集条件 None/Always/NonPass |
| 需求编号、步骤备注、显示证据 | 归档事实不删除 | `result.requirementId` / `result.reportNote` / `result.showEvidence` |
| 原始帧/信号记录及质量/Drop 计数 | 在真实归档器接线后必须可校验 | P0 仅定义原始归档 Finalizer 和健康结果；不新建硬件 RX 线程 |
| HTML/JSON/CSV | 由不可变 RECORD 同源生成 | HTML 显示策略不能篡改底层日志与报告判断 |

`TestReportSettings` 采用 TEST 文档内 **可选属性**（Title/CaseNumber/Description），旧 TEST 文档缺省时 JSON codec 不写 `ReportSettings`，避免无修改的旧文件出现字段扩充导致计划指纹意外变化。新增设置走原保存、修订与 SHA256 冻结通路。

### 2.3 状态区分

- `RealStepStatus.Completed` 代表当前步骤 API/操作完成；仅显式 AssertSignal/WaitUntil/Expect 合格时产生 TEST 判断，Write ACK / Read / Wait 不直接判定 DUT PASS。
- `RealTestVerdict.Passed` 表示 TEST 解释器依据该次配置得到的断言结果；若记录缺少真实可信原始采集与已验证 EvidenceRef，`RunCompletion` 不允许正式 `Verdict.Pass`。
- `RunCompletion` 与 `RecordPersistenceState` 是不同维度；写入失败/进程异常可保留 Writing/Interrupted 前缀，不能凭未落盘内存结果生成出厂 PASS。
- 设备无帧、超时、解码错误、断连、DBC 来源不匹配、数据丢失、操作取消必须各自留下实际状态，不能用空字符串当数值、不能用后续常量断言抹掉前一条物理数据缺失。
- 原始观测 `HostArrival/HostArrivalOnly` 不得冒充设备硬件同步时间；Duration 使用 .NET Stopwatch 单调耗时，不把 UTC 差值冒充高精度测量。

## 3. 实施后的唯一调用链（本专项后端源码）

```text
HOST (待批准接线)：实际工程/DBC/身份/工位预检，创建真实 RunRecord
    |
    v
TEST PlanCompiler：冻结已有 TestDocument / ResultOptions / ReportSettings
    |
    v
Application.RealTestRecordSession.ExecuteAsync(prepared)
    | 核对真实 RunSnapshot.PlanFingerprint == prepared.Fingerprint
    | RunRecordWriter.BeginAsync => SQLite durable
    | RunFact RUN_BEGIN / TEST_CONTEXT（绑定 CaseAttemptId/冻结用例信息）
    |
    v
RealRunCoordinator.StartAsync(prepared, RunId, IRealRunEventSink)
    | 每次 StepStarted => 按序写 STEP_BEGIN durable
    | 原有真实 ITestSignalPort / ITestActuationPort（不改设备协议）
    | DBC SignalRef == 冻结映射？不匹配则 INVALID / INCONCLUSIVE
    | StepCompleted => 原 RunStepFact（Occurrence/Parent/Phase/Time/
    |                  Expected/Actual/Unit/Quality/DbcSource/Evidence）
    |               => STEP_END 审计 durable
    | Prepare gate、Test、Cleanup 依旧由原执行器控制
    |
    v
RunCompleted:
    | 系统错误及异常事实写入 RunFact
    | IRealRunEvidenceFinalizer（真实 HOST 负责持续采集、fsync、原附件绑定）
    | 审计 EVIDENCE_CAPTURE（完整性/DroppedEvents）
    | 与原 ArtifactRepository SHA256 逐项核对
    | 不完整则 EVIDENCE_GATE_NOT_MET / Unknown + Inconclusive
    | 原 TEST 断言结果仍作为 RUN_TERMINAL 流事实
    | RunRecordWriter.CompleteAsync（原 RECORD 终态）
    v
RunHistoryQuery / RunReportExporter (同一个 SQLite)
    -> HTML、JSON、CSV 生成文件（重试时读原历史；不重算 DUT verdict）
```

实际 `AutoTest.Desktop/Testing/RealTestWorkbenchService` 尚未引用此 Application 适配器，禁止把上图视为已经发生的真机链路；这是**后端可供 HOST 接线的完整接口路径**。

## 4. 详细的证据门禁与 FAIL-CLOSED 设计

### 4.1 原始归档接口

原 `IRealRunEvidenceFinalizer` 的单一职责仍是 **HOST 实现**：结束本次真实原始 CAN/DBC/DEVICE 连续采集，确认文件字节已 flush/fsync，使用已存在 `ArtifactRepository.PutAsync` 将采集件内容地址化，并以 **真实** EvidenceRef 标识调用 `RunRecordWriter.AttachArtifactAsync`。

新 `RunEvidenceCaptureResult` 声明 `RecordingComplete`、`DroppedEvents`、`IncompleteReason`。写入前必须先获得这些真实结果；此专项没有实现新的 USB FrameObserved 回调或假造设备采集。

### 4.2 为什么更改了正式 PASS 门禁

之前 Draft 实现允许 `RealTestVerdict.Passed` + `evidenceArchiveIncomplete=true` 同时存在，并可能将 RECORD 正式终态写成 Completed/Pass。对于 Issue #64 的正式出厂报告目标，这是会造成错误放行解读的风险。现在只在新适配器中规定：

```text
FormalPassAllowed =
    TEST verdict Passed
    AND verified evidence finalizer reports RecordingComplete
    AND DroppedEvents == 0
    AND at least one trusted signal observation carrying frozen DBC provenance
    AND at least one real EvidenceRef
    AND every referenced EvidenceRef resolves to present SHA-verified Artifact
    AND no recorded artifact is Missing / Corrupt
```

若 TEST 报 Passed，但门禁不成立：**保留 TEST 解释器原始 Passed 事实与逐步测量**；RECORD 终态提交 `RunState.Unknown + Verdict.Inconclusive + FailureReason=EVIDENCE_GATE_NOT_MET`，并写 `EVIDENCE_GATE_NOT_MET` 事实（缺失计数、丢帧状态、可信观测缺口）。这不篡改历史测试判断，也不会让没有归档附件的记录变成正式 DUT PASS。项目管理仍有权审查其与生产统计的边界（本专项不改 ProductionWorkflow）。

FAIL/ERROR/ABORT 不会因归档缺失被包装为 PASS；其报告明确证据不完整。归档本身抛出异常时转 Unknown/Error（或在 SQLite 失败时保留 Writing/Interrupted），而不是提交原 Pass。

### 4.3 DBC 来源防假阳性

除 HOST 绑定时的 `LiveDbcSignalService.BindAsync` 外，TEST 内核还在 Read/WaitUntil/AssertSignal 收到结果后核对 `SignalRef` 的 AssetId、Revision、SHA256、Role、FrameId、SignalName、Unit 与冻结 `TestTargetMapping`，不匹配即 `DBC_SNAPSHOT_MISMATCH`、Invalid/NoData 与运行 Inconclusive。即使不匹配的信号数值恰好等于预期，也不能作为判定通过。

单纯 Read 得到 NoFrame/Timeout/InvalidData 也会标记运行 `incomplete`；后续与 ECU 无关的常量 Expect 不能把整个 Run 变成 Passed。

### 4.4 启动恢复与报告输出

仍使用原 `RecordDatabase.RecoverInterruptedRunsAsync`，但只能由 HOST 在独占数据库启动阶段调用：Writing 置 Interrupted/Unknown，已提交事实保留。不能在正常运行中误将正在写入的 Run 置中断。导出器拒绝为 Writing Run 生成“最终报告文件”，可以从已恢复的 Interrupted 或 Committed 历史重导出。HTML/JSON/CSV 单个文件用临时文件写入、flush 并原子替换；三个文件不是一个原子事务，失败必须显示实际导出失败与可重试状态。

## 5. 本次实际源码清单

| 文件 | 改动 |
|---|---|
| `HOST/src/AutoTest.Testing/Authoring/StepResultRecordingOptions.cs` | 类型化 `result.*`、默认值及约束 |
| `HOST/src/AutoTest.Testing/Authoring/TestReportSettings.cs` | 用例级报告标题/编号/说明的独立类型 |
| `HOST/src/AutoTest.Testing/Authoring/TestDocument.cs` / `TestDocumentCodec.cs` | 单一 TEST DSL 内新增可选 metadata、兼容旧存储 |
| `HOST/src/AutoTest.Testing/Preparation/PlanCompiler.cs` | 对新增配置做硬件动作前校验 |
| `HOST/src/AutoTest.Testing/Runtime/RealRunEventSink.cs` | 可等待 STEP_BEGIN/STEP_END/RUN_END 观察口 |
| `HOST/src/AutoTest.Testing/Runtime/RealRunCoordinator.cs` | Occurrence、父子/循环/Phase、时间、DBC 质量来源校验、丢帧/缺 Read 禁止假 PASS |
| `HOST/src/AutoTest.Records/Model/RunFact.cs` / `Storage/RecordDatabase.cs` | 在原不可变 StepPayload 中增 CaseAttempt、Occurrence、测量、失败代码、DBC provenance，可选字段向后兼容 |
| `HOST/src/AutoTest.Records/Exports/RunReportExporter.cs` | 同源 HTML/JSON/CSV、标题、CaseAttempt、采集健康、缺失/损坏证据提示、原子文件写出 |
| `HOST/src/AutoTest.Application/TestRecord/RealTestRecordSession.cs` | 既有 TEST/RECORD 实施桥、RUN_BEGIN、TEST_CONTEXT、事实写入、原证据最终核验门禁 |
| `HOST/tests/AutoTest.Testing.Tests/RealOccurrenceTests.cs` | 真实端口合同的合成输入测试源码 |
| `HOST/tests/AutoTest.Records.Tests/StepOccurrenceReportTests.cs` | SQLite 往返、旧记录、报告合同的合成数据测试源码 |
| `HOST/tests/AutoTest.Application.Tests/RealTestRecordSessionTests.cs` | TEST→RECORD→报告、冻结元数据、案例身份与证据门禁合同测试源码 |

整个分支没有修改主控/按键板/UDS/FLASH/真实 USB 协议，没有新增第二套数据库或 TEST 解释器，没有写 `HOST` 或 `main` 分支，也没有改 WPF 视觉。

## 6. 软件测试与正式验收矩阵（不混淆）

| 场景 | 当前代码/测试覆盖意图 | 真机证据 |
|---|---|---|
| 单条真实 DBC 信号符合预期 | 测量来源可追溯，记录/报告均指向同一 Occurrence | 待用户真实 ECU |
| DBC 信号判定 FAIL | Fail 判定、Expected/Actual 和失败码 | 待用户真实 ECU |
| 信号 Timeout/NoFrame/Disconnected/DecodeError | Inconclusive，保留实际状态，不创建假来源 | 待用户真实 ECU |
| 测量值匹配但 DBC ID/版本/信号不匹配 | 阻止错误 PASS，保留 `DBC_SNAPSHOT_MISMATCH` | 待用户真实 ECU |
| Read 无数据 + 后续常量 Expect Pass | 总体仍 Inconclusive | 合同测试源码已加入 |
| Loop 同一 StepId 多次 | 单独 StepOccurrenceId，统一 CaseAttemptId | 合同测试源码已加入 |
| 准备阶段失败、原有安全清理、取消 | 承接现有 TEST 的控制与 RECORD 的终态记录；需现场核验 | 待用户真实 ECU |
| 无原始证据归档却 TEST Passed | RECORD Unknown/Inconclusive、EVIDENCE_GATE_NOT_MET | 合同测试源码已加入 |
| 原始记录 Dropped > 0 | 正式 PASS 被阻断 | 合同测试源码已加入 |
| Artifact Put/Attach/校验失败 | 不允许原 PASS 正常提交；异常终态记录 | 合同测试源码已加入 |
| Legacy TEST / Legacy RunStepFact | 缺省可读，不凭空制造历史身份 | 合同测试源码已加入 |
| HTML / JSON / CSV | 同一不可变 RECORD 回读，包含原始失败信息和证据缺口 | 合同测试源码已加入 |

**测试代码中的端口与附件明确是合成合同输入**：只允许用于验证序列化、判断和故障路径；禁止当成实体 ECU、真实 CAN、电磁阀、安全控制或生产 PASS 验收。

### 6.1 本次验证事实

- GitHub 源码写入与逐文件回读：**完成**。
- 功能改动所在目录、与 `HOST` 基线的 diff：**完成**。
- 14 个关键 C# 文件的词法级括号配对/冲突标记静态检查：**PASS（不等于 C# 编译）**。
- Windows `dotnet restore` / `dotnet build` / `dotnet test`：**NOT_RUN**，当前执行环境没有 .NET SDK。
- Windows WPF 正式启动：**NOT_RUN**。
- 实体 ECU/USB/CAN 数据与真实报告：**NOT_VERIFIED**。
- 实体 HOST Desktop→Application adapter→SQLite 的实际入口：**NOT_WIRED**。
- HOST 原始 CAN/DBC/DEVICE 持续归档与 SHA 附件：**NOT_WIRED**。

## 7. 给 PM、HOST、PROJECT、UI 的准确交接接口

### 7.1 PROJECT/HOST 输入 RunRecord，不允许 AI 杜撰工程快照

HOST 调用 `RealTestRecordSession` 前必须在真实 PROJECT 资产和实际 station 上创建 `RunSnapshot(ProjectSnapshot, StationProfile, PlanFingerprint, LockedAssets)`，确认真正 SHA，规范 `SessionIdentity` 的扫码/开发来源，填写真实 HostBuildIdentity 并标记 `RecordRunMode`。无扫码 Development 的合法身份由 PM/PROJECT/HOST 明确；无身份不可创建虚构 Production Run。

### 7.2 HOST Desktop 真实接线（当前尚未修改）

在已有 `RealTestWorkbenchService` 的真实 PRE-FLIGHT 完成后：
1. 复用原工程和已冻结 PreparedTestPlan；
2. 构造完整 RunRecord 与现有 RunRecordWriter、ArtifactRepository、RunReportExporter；
3. 在实际 CAN/DBC/DEVICE 采集会话里启动有边界的**持续磁盘采集**，记录掉帧和覆盖计数；由 HOST 编写 `IRealRunEvidenceFinalizer` 返回真实 CaptureResult 并绑定 EvidenceRef；
4. 调用 `RealTestRecordSession.ExecuteAsync(existing RealRunCoordinator, prepared, reportFolder)`；不要再把旧临时 JSON 作为新的正式结果源；
5. UI 显示 `RealRunResult.Verdict` 与 RECORD 的 `RunHistoryEntry.Verdict/Persistence/FailureReason` 为**不同维度**，报告文件路径只能在真实导出成功后宣告完成；
6. HOST 在独占启动时 `RecoverInterruptedRunsAsync`，历史视图必须能读到失败、中断和报告可重导出情况。

这一变更不由本专业未经评审自行写入 Desktop；主控协议也不用为此修改。

### 7.3 UI 的实际后端可编辑合同

TEST Step Inspector 可绑定 `StepResultRecordingOptions.Read/Apply` 与现有判定属性；测试程序级报告设置可绑定 `TestDocument.ReportSettings`；运行界面通过已经存在的 `RunRecordWriter/RunHistoryQuery/RunReportExporter` 只读查询事实与结果，不另建一套报告属性语义。没有现场真实证据的报告不能出现绿色生产 PASS。

### 7.4 跨专业待项目管理批准的点

- 是否认可“原始测试断言 Pass + 证据门禁不满足 → 正式 RECORD Unknown/Inconclusive”的**新后端 PASS 门禁**；本次仅改变尚未接 HOST 的新适配器，未修改既有生产统计和出厂放行代码。
- HOST/PROJECT 的真实 `RunSnapshot`、未扫码 Development 身份与模板资源归属。
- HOST/DEVICE 对 FrameObserved/SignalReceived 连续采集、drop/partial、EvidenceRef 与原 ArtifactRepository 的唯一对接实现及安全时序。
- 是否授权 HOST/INTEGRATION 在 Windows Build/Test 实际通过后定点接入此 PR；批准不等于真实 ECU 验收。
- 公共流程、项目多用例、WAVE/UDS/FLASH 的专项接口保留 P1，禁止为 TR-01 顺手改生产逻辑。

## 8. Windows Codex 的强制验证回执

需要在隔离、干净 Windows 工作树以 **当前 PR 精确 SHA** 执行以下命令，**不要开启 CI，不要对实体 ECU 运行测试桩，不要自行合并**：

```powershell
git fetch origin feature/test-record-backend-20261119
git switch --detach origin/feature/test-record-backend-20261119
git rev-parse HEAD
cd HOST
dotnet restore AutoTest.sln
dotnet build AutoTest.sln -c Release --no-restore
dotnet test tests/AutoTest.Testing.Tests/AutoTest.Testing.Tests.csproj -c Release --no-restore
dotnet test tests/AutoTest.Records.Tests/AutoTest.Records.Tests.csproj -c Release --no-restore
dotnet test tests/AutoTest.Application.Tests/AutoTest.Application.Tests.csproj -c Release --no-restore
```

应报告精确 HEAD、每条命令 exit code、错误数/警告数、运行/通过/失败/跳过测试数、实际原始日志文件和 WPF 启动结果。不要只写 `ALL_PASS`。Windows 验证后由 HOST 接线，再由用户真实 ECU 实测按 PASS/FAIL/超时/断连/Loop/中断路径签收。

## 9. 本专项交付结论

**P0 私有后端合同、应用适配与报告模块的源码修改已集中提交，可交项目管理审查。** 但不得声称“整个 AutoTest 真实测试报告闭环已正式上线”：Windows 构建、真实 Desktop 接线、原始现场采集、真实 ECU 和最终合并均有独立门禁。

请 PM 以 **Draft PR #65 + 本文 + Issue #64** 审核责任范围、证据判定、兼容性、接口归属与测试验证真实性。未批准前继续保持 Draft；不擅自改 `main`、`HOST` 分支或硬件协议。

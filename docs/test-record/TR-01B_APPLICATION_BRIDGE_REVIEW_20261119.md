# AutoTest｜TEST-RECORD TR-01B 应用层闭环适配专项审查记录

- 日期：2026-11-19
- 分支：`feature/test-record-backend-20261119`
- 审查对象：Draft PR #65 / Issue #64
- 状态：`BACKEND_BRIDGE_CODE_SUBMITTED / HOST_DESKTOP_NOT_WIRED / PM_APPLICATION_REVIEW_PENDING / WINDOWS_BUILD_NOT_RUN / ECU_NOT_VERIFIED`
- **不能当作已完成真实 ECU E2E 验收或获准合并。**

## 一、基于现有项目引用的最小适配

核对 `HOST/src/AutoTest.Application/AutoTest.Application.csproj`：该项目早已引用 `AutoTest.Testing`、`AutoTest.Records`、`AutoTest.Projects`，因此不增加任何新的项目、执行器、设备会话或数据库。

新建 **`HOST/src/AutoTest.Application/TestRecord/RealTestRecordSession.cs`**：`RealTestRecordSession : IRealRunEventSink`。这是可供 HOST 在批准后调用的内部应用编排适配，不是新的 WPF 入口。

主要时序：

1. HOST（尚未接线）提供已经真实预检、确认安全、具有合法 ProjectSnapshot/StationProfile/锁定 DBC/真实身份策略的 RunRecord，及 PreparedTestPlan；必须一致匹配 RunSnapshot.PlanFingerprint。
2. `RunRecordWriter.BeginAsync(record)` 在首次 TEST 步骤和物理写入前耐久提交；失败即不启动解释器。
3. 调用唯一 `RealRunCoordinator.StartAsync(plan, record.RunId, adapter, ct)`。
4. 对包括 Disabled/Skipped 的每个 occurrence 先 `RunFact STEP_BEGIN`，并等待持久化确认。非持久结果不能继续执行实际步骤。
5. 完成步骤先追加 **原 Record DB 的 `RunStepFact`**，包含 per-Step Attempt、StepOccurrence/Parent/Phase/LoopPath/时间和完整实测信息，再添加 `RunFact STEP_END`。原生 SignalObservation 绑定 `EvidenceRef`、DBC AssetId/Revision/SHA、CAN role/id、信号名、单位、原始时间源/质量/单调计数器。
6. `Write` 的设备 ACK、`Read` 的操作完成属于事实，不被标为 DUT 判断；明确的 `Expect/WaitUntil/AssertSignal` 成功或失败才是判断事实；无数据、失帧、异常另记不确定事实。
7. TEST 完成且仍处于 Writing 时，调用可选的 **`IRealRunEvidenceFinalizer.FinalizeEvidenceAsync`**，由未来 HOST 实现原始 CAN/DBC/DEVICE 证据流的实际落盘和原 `ArtifactRepository` 的附件关联。若归档回调抛错，不允许提交原判定 PASS；转入 Unknown/Error 异常恢复流程。此处只是**接口预留**，不是已经写好真实设备采集。
8. 完成证据归档后，才通过既有 `RunRecordWriter.CompleteAsync(RunCompletion)` 按 RealTestVerdict 提交；使用既有 `RunReportExporter.ExportFilesAsync` 生成 HTML/JSON/CSV。
9. 解释器若在已 Begin 后意外抛错，尽可能持久化 `Unknown/Error` 与报告；如数据库也损坏/不可写，则不假装已提交，保留已落盘前缀并等待 exclusive startup `RecoverInterruptedRunsAsync`。
10. 报告显示逐次失败代码、诊断、实际值/质量、DBC 来源、证据归档不完整提示。**只有 EvidenceRef 而无 ArtifactRepository 已验证的原始附件时，明确标为未归档，不声称原始 CAN 证据完整。**

## 二、源码增量（本专项分支）

- TEST 内核：`RealRunCoordinator.cs`、`RealRunEventSink.cs`，未拆分执行器。
- TEST 编辑配置：`StepResultRecordingOptions.cs`，编译检查依旧通过现有 `PlanCompiler`。
- RECORD 数据合同：`RunStepFact` 附加可选 `RunSignalProvenance`，旧事实不造 Occurrence/DBC 版本。
- RECORD SQLite：`StepPayload` 扩展可选 JSON 字段，继续使用原 Schema 1、不可变约束、EventId 唯一性；无新的数据库文件。
- RECORD 报告：导出包含 FailureCode/Detail、DBC/Sample 来源和未归档 EvidenceRef，敏感 HTML 内容编码，不靠报告重新计算 Verdict。
- Application 编排：`RealTestRecordSession` 使用既有 Testing/Records 项目引用，不改 ProductionWorkflow，不增加 WPF/USB/CAN/KEY/UDS/FLASH 业务行为。

## 三、测试源码：仅存在，Windows 未执行

- `HOST/tests/AutoTest.Testing.Tests/RealOccurrenceTests.cs`：Loop 多次 occurrence、冻结结果配置、丢帧禁止假 PASS、无持久化开始时拒绝物理写入、原生 DBC 质量/证据。
- `HOST/tests/AutoTest.Records.Tests/StepOccurrenceReportTests.cs`：同一 StepId 两次存储/导出、旧记录兼容、非法时间范围拒绝。
- `HOST/tests/AutoTest.Application.Tests/RealTestRecordSessionTests.cs`：**整个内部链**（以显式 UNIT-TEST 端口和 UNIT-TEST-DUT 为输入）验证 PASS/FAIL/NoData、真实 Record DB 内容、3种报告、DBC version/clock、raw evidence 未归档提示、归档成功/失败先于最终提交、指纹不匹配、意外异常终态、Read 非判断、Skipped 成对审计。

以上单元/集成**合同测试输入是假端口**，只证明代码路径与数据库/报告转换的正确性；绝不可作为真实 ECU 判定、接线、安全或物理控制的验收证据。

## 四、项目管理必须明确批准的交接边界

- **Application adapter 代码审核**：应用层已有 TEST 与 RECORD 的引用，新增只是内部适配；仍需 PM/INTEGRATION 对 `AutoTest.Application/TestRecord` 的责任域变更确认。若不批准，此文件保留 Draft 而不合并。
- **HOST 实际调用审查**：`Desktop/Testing/RealTestWorkbenchService` 未改；HOST 负责用真实工程快照和开发/扫码身份实例化 RunRecord，并将 adapter 替换独立 JSON 结果入口。必须按用户现场机型、现有 USB/DBC 业务规则进行预检授权。
- **实时 CAN/DBC/DEVICE 原始证据**：`RealRunEvidenceRecorder` 仍是最多 100000 事件 RAM 最终 JSON；本轮未修改。真实 FrameObserved/SignalReceived 写入原 Record 持久化并关联 ArtifactRepository，需要 HOST/DEVICE/RECORD 协作接口审查；不假装已经实现持续原始流。
- **生产隔离**：无扫码开发运行的真实 DUT 标识/来源规则需由 HOST/PROJECT/PM 明确。禁止模拟扫码指纹、虚构物理身份和按 Development PASS 自动进入出厂统计。
- **写入失败与现场处理**：本适配在关键写入失败时不放行下一步；现场要验证夹具/周期 CAN 取消与清理、RunId/StepId/Occurrence/数据库和报告、异常/超时/断连。
- **报告事实与完成状态**：原运行判断和存储成功是不同维度。暂不把存在未归档原始 EvidenceRef 的报告称为完整正式验收；报告已列证据归档缺口，需要 PM 定夺生产门禁是否必须同时阻止判定放行。

## 五、Windows Codex 验证单（必须记录准确 SHA，不能把代码存在当 PASS）

在 Windows 专用、无未提交修改的工作树，以 PR #65 当前精确 HEAD 拉取，不覆盖任何本地开发分支，不进行 CI 或模拟设备验收：

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

Codex 应将 Restore / Build / 三项测试的 exit code、失败类和原始日志与 HEAD 一起反馈。测试成功**依旧不等于**已在现场 ECU 实测；不要运行 Mock 硬件验收、不要改固件、不要未经授权合入 HOST/main。

## 六、当前门禁结论

- GitHub Draft PR #65：已建立，非合并状态。
- 现有 DB 和 TEST→RECORD 映射：代码已提交且基础静态检查；**没有编译通过证据**。
- Desktop 真实入口、可归档原始 CAN 证据、扫码/统计/生产放行：**尚未接线**。
- Windows Build/Test：`NOT_RUN`。
- 实体 ECU 正反例及中断验证：`NOT_VERIFIED`。
- PM Application/HOST/PROJECT/证据接口审查：`PENDING`。

**下一正式门禁**：完成准确 SHA 的 Windows 构建/测试修复，之后经 PM 批准再定点接 HOST Desktop，并由用户现场获取真机证据。

# AutoTest｜TR-01C TEST→RECORD 实施复核及证据门禁风险

日期：2026-11-19  
分支：`feature/test-record-backend-20261119`  
审查基线：`5c58b5ebf2ec44d9f802934db5dbf326bde4118f`  
状态：`BACKEND_BRIDGE_SOURCE_AUDITED / PM_EVIDENCE_POLICY_REVIEW_REQUIRED / WINDOWS_TEST_NOT_RUN / REAL_ECU_NOT_VERIFIED`

## 1. 本轮完成的源代码检查

已复核最新增加的 `HOST/src/AutoTest.Application/TestRecord/RealTestRecordSession.cs`，不是再建立另一套 adapter：

- `ExecuteAsync` 先核对冻结 `PreparedTestPlan.Fingerprint`，再通过原 `RunRecordWriter.BeginAsync` 建立持久 RunId；交由唯一 `RealRunCoordinator` 执行。
- `StepStartedAsync` 使用 `RunFact` 保存 STEP_BEGIN 并等待 durable receipt；`StepCompletedAsync` 先写 `RunStepFact`，再追加 STEP_END；同 StepId 的 Attempt 与 StepOccurrenceId 分离。
- 信号值沿 `SignalObservation` 原样带出 DBC AssetId/Revision/SHA、FrameId、SignalName、Unit、TimePoint、TimeQuality、EvidenceRef。
- `RunCompletedAsync` 对确有归档提供者的运行在数据库提交前调用 `IRealRunEvidenceFinalizer`；回调抛出时降为 Unknown/Error 异常路径；`RunReportExporter` 从原 SQLite 生成正式文件。
- `RealRunCoordinator` 对 Disabled/Skipped 已调整为先 STEP_BEGIN 再 STEP_END；异常处理尝试运行清理。
- 上述为代码结构和调用路径审查，**并非编译/测试/实机验证通过**。

## 2. 进一步新增的软件合同测试

`HOST/tests/AutoTest.Application.Tests/RealTestRecordSessionTests.cs` 新增 `MissingOrDisconnectedSignalCannotCommitPassAndRetainsQuality`，覆盖：

- `TestSignalStatus.Timeout`
- `TestSignalStatus.Disconnected`
- `TestSignalStatus.DecodeError`

要求 TEST Inconclusive、RECORD Unknown/Inconclusive、RunStepFact 的原状态和 NO_DATA 失败码保留、没有凭空制造 SignalProvenance/EvidenceRef，生成的 JSON 可显示该质量状态。**新测试尚未执行；没有硬件设备参与。**

## 3. 阻断正式验收的证据门禁问题（需 PM 确认）

当前 `RealTestRecordSession.RunCompletedAsync` 仅在提供 `IRealRunEvidenceFinalizer` 时执行归档。未提供归档器时：

- 完整的真实信号值及其 `EvidenceRef` 可能已经写入 SQLite；
- `RunReportExporter.ExportJsonAsync` 会正确显示 `evidenceArchiveIncomplete=true`；
- 但在 TEST 已给出 Passed 的情况下，`RunCompletion` 仍可能写入 `RunState.Completed + Verdict.Pass`；
- 现有合同测试 `DatabaseAndReportsReflectSameExecutedOccurrences` 明确允许“软件事实 PASS，但原始证据未归档”的示例。因此要分清 **TEST 判断结果** 与 **正式交付/生产可接受的完成条件**。

这不代表已经产生假测量值，但意味着**报告明确缺失原始附件，数据库中仍可出现 PASS**。根据 Issue #64 对缺失证据应明确 UNKNOWN/INCONCLUSIVE/ERROR、正式报告完整性的要求，须由项目管理选择统一策略，而不能让 UI 或不同模块自行决定。

建议保持两个正交维度，避免篡改测试事实：
1. `TestVerdict`：TEST 对确实收到的 ECU 观测和比较的原始判断，不因导出样式被重判。
2. `EvidenceCompleteness/ReleaseEligibility`：来自原始附件是否持久化、匹配 EvidenceRef、SHA256 校验、Dropped/Incomplete、设备与工程版本身份完整度；正式出厂放行要求两者均通过。
3. 如果业务要求“数据库不得出现无证据 PASS”，应明确规定有证据引用而无法归档时 RunCompletion 终态 `Unknown/Inconclusive`，另外保留 TEST 原始 Passed 事实。需同步审查 RECORD/Production 统计避免回归。

**尚未修改上述 PASS 政策**，避免未经 PM 批准改变已存在的正式产线历史/判定合同。

## 4. 仍需 HOST/PROJECT 的准确接口

- HOST 当前 `RealTestWorkbenchService` 尚未引用新 `RealTestRecordSession`；真实 TEST 编辑器路径仍只保存旧 `test-records/{RunId}.json`。必须先确认真实快照与无扫码 Development 身份合法来源，再由 HOST 负责定点接线。
- HOST `RealRunEvidenceRecorder` 当前还是 100000 条 RAM 末端 JSON，没有每条原始 CAN/DBC/DEVICE 证据 durable、且尚无完整的 ArtifactRepository/EvidenceRef 附件对应关系。
- `RecoverInterruptedRunsAsync` 需在应用独占启动阶段调用并显示恢复的 RunId/Unknown；不能由 TEST 运行过程中随意调用。
- 成果经 Windows Codex 在准确 PR HEAD 运行 Restore、Build、TEST/RECORD/Application 专项测试及 WPF 启动后，才能进入用户现场真实 ECU 验收。

## 5. 当前可证明门禁

- GitHub 专项分支源码提交：**已完成**。
- 此轮新增异常质量/断连/超时测试代码：**已提交**。
- Windows Restore/Build/Test：**NOT_RUN**（当前执行环境没有 dotnet SDK）。
- 真实 USB/CAN/DBC/ECU 运行：**NOT_VERIFIED**。
- 真实 Desktop→SQLite→报告：**NOT_WIRED**。
- 原始证据持续落盘：**NOT_WIRED**。
- PM 审核 Application 边界及“无附件仍存在 PASS”的正式门禁：**PENDING**。
- Draft PR #65：**保持 Draft、未合并**。

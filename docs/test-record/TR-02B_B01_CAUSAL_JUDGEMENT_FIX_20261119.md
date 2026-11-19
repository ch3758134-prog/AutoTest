# AutoTest｜TEST-RECORD PR #65｜B-01 判定与原始证据因果绑定定点修复（2026-11-19）

## 交付身份与结论

- 来源：项目管理审查 [TEST-RECORD-PR65-TECHNICAL-REVIEW](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/new-baseline-20261112/research/TEST-RECORD-PR65-TECHNICAL-REVIEW-20261119.md)
- 阻断项：**B-01：任意有效 Read 的证据不能为无关 Expect 兑换正式 ECU PASS**。
- 改动目标：[Draft PR #65](https://github.com/ch3758134-prog/AutoTest/pull/65)，开发分支 `feature/test-record-backend-20261119`。
- 结论：**B-01 定点源码修改及专项回归测试源码已提交，等待 Windows Codex 实际编译与测试，以及 PM 复查。不得自行把本报告视为源代码测试已通过。**
- 未修改 `main`、`HOST`、`Desktop`、`DEVICE`、`PROJECT`、真实 CAN/USB 协议、硬件行为和生产统计。

## 1. 为什么原来的 Run 范围 Any(valid signal) 不足以证明 PASS

旧实现只验证本次 Run 有一个可信 DBC 观测、其证据已归档、且最终采集健康；但观测可能来自 `Read(A)`，真正令 `asserted=true` 的却是 `Expect(1 == 1)`。此时即使 A 的原始字节文件 SHA 校验通过，也不能证明常量比较是 ECU 判定。**附件存在、TEST Passed、判定证据来自同一次 Run，是三个独立条件，不构成因果证明。**

## 2. P0 支持与明确 fail-closed 边界

P0 正式 ECU PASS 只认可**确实执行且成功判断了冻结 DBC 信号**的 `AssertSignal` 或 `WaitUntil`。必须是 **Test phase** 的 `RealStepStatus.Completed`，带有效 `SignalObservation`、自身 `EvidenceRef`、自身 `StepOccurrenceId`，观测值与本次成功比较中的 Actual 一致，观测状态 Valid、无 gap、可靠质量，并与冻结 `TestTargetMapping` 的 AssetId、Revision、SHA256、BusRole、FrameId、SignalName、Unit 完全匹配。

`Read` 本身仅采集数据，不作 DUT 判定；`Expect(1 == 1)` 无物理判断含义。**Read → Expect($variable) 暂不具备变量级 provenance/依赖图，P0 明确不授予正式 ECU PASS**（即使变量的值真实来自刚才的 Read）。TEST 引擎的原始 Parsed/Failed 仍保留在运行时及审计中，不因此重新定义用户已有的 TEST DSL；正式 RECORD 另行计算证据验收门禁。将来如需允许派生比较，须在 TEST 变量中传播来源 StepOccurrence、映射指纹、可用观测和证据，并由 PM 另行批准新的证明类型；本次**不伪造**此功能。

保守性：只要 Test phase 本次执行有任何成功但未直接证明物理观测的 `Expect`，P0 就不给该运行正式 ECU PASS，即使另一个 `AssertSignal` 真正通过。不能允许非物理断言借用物理断言或其附件。Prepare/Cleanup 中不贡献 P0 Test phase 证明的普通运算不作为独立正式 ECU 判定。

## 3. B-01 判定链的源码实现

### 3.1 StepOccurrence 内的直接证据标签

`HOST/src/AutoTest.Application/TestRecord/RealTestRecordSession.cs`：
- 在启动真实 TEST 前，从准备阶段校验的 `PreparedTestPlan.RestoreDocument()` 构建 `StepId → Frozen TestTargetMapping`；
- 每次 `StepCompletedAsync` 根据 **本 StepOccurrence 实际成功判定**、直接观测值、Valid/HasGap=false/质量、非空 EvidenceRef 与 **同一个 StepId 的冻结 DBC 映射**产生 `JudgementEvidenceKind="DirectDbcSignal"`，不允许 `Read`/任何 `Expect` 生成标签；
- 同一 StepOccurrence 的 `RunStepFact` 承载 `StepId`、`CaseAttemptId`、`StepOccurrenceId`、Time/Quality/Actual/Expected、SignalProvenance/EvidenceRef 和可选证明标签；
- `STEP_END` 追加式日志一并包含证明标签，事件记录不能以 UI 展示过滤关闭。

`HOST/src/AutoTest.Records/Model/RunFact.cs` 的 `RunStepFact.Validate` 对 `DirectDbcSignal` 做结构约束；`Storage/RecordDatabase.cs` 的原 SQLite JSON StepPayload 加可选 `JudgementEvidenceKind`，旧数据无该字段仍可读；`Exports/RunReportExporter.cs` 将它列入 HTML/JSON/CSV。

### 3.2 RUN_END 只按实际判定 occurrence 验证

`RealTestRecordSession.RunCompletedAsync` 在完成真实原始证据 finalizer 后：
1. 从原 RECORD 读出本次 Run 的已提交 `RunStepFact` 与附件核验状态；
2. 从 TEST `RealRunResult.Facts` 找出本次 Test phase 所有成功执行的判定步骤 `AssertSignal`、`WaitUntil`、`Expect`；
3. 对每个成功判定，用 `StepOccurrenceId` 唯一定位已持久化的 STEP 事实，并要求 `StepId` 与 `CaseAttemptId` 完全一致；
4. 要求该 STEP 明确标记直接 DBC 判定，同时要求实际运行时的 Observation/Actual 与数据库 Expected/Actual/ObservedUtc、EvidenceRef、DBC 资产指纹/版本/Role/FrameId/SignalName/Unit 一致，附件 ID 对应 VerifyAsync=Present；
5. 至少有一项通过证明，且**无其他未证明的成功 Test-phase 断言**，再结合既有整 Run 的 capture complete、DroppedEvents==0、零 orphan/损坏证据，正式 RECORD 才可能写 `Completed/Pass`。
6. 若 TEST 原 Verdict Passed 但证明链不成立，则保留原 TEST Verdict 在 `RUN_TERMINAL` 审计中，写 `EVIDENCE_GATE_NOT_MET`，包含 successfulAssertionCount、provenDirectDbcAssertions、unprovenAssertions 等事实，RECORD `Unknown/Inconclusive`。不转换为 PASS，也不篡改测试执行器判断。

**这不是从报告重判 ECU**：TEST 作信号比较；RECORD 只验证从真实成功判定到已归档证据的完整来源证明，不得凭任意观测推导一个新测试判定。

## 4. 回归矩阵（测试源码已写入，未执行）

| 场景 | 预期 TEST | 预期 RECORD |
|---|---|---|
| AssertSignal 得到冻结 DBC 合法值，自己的 EvidenceRef 实际归档 | Passed | Completed / Pass，标记 DirectDbcSignal |
| WaitUntil 得到冻结 DBC 合法值，自己的 EvidenceRef 实际归档 | Passed | Completed / Pass，标记 DirectDbcSignal |
| Read(有效无关信号) + Expect(1==1)，Read 证据完整归档 | Passed | Unknown / Inconclusive / EVIDENCE_GATE_NOT_MET |
| Read(有效信号) + Expect($observed==2)，Read 证据完整归档 | Passed | Unknown / Inconclusive（本 P0 不支持变量 provenance） |
| AssertSignal(真) + Expect(1==1)，全部证据归档 | Passed | Unknown / Inconclusive（不能让另一断言借证据） |
| AssertSignal 收到与冻结 DBC 不匹配的帧 | Inconclusive | 非 Pass；保存 DBC_SNAPSHOT_MISMATCH |
| LostFrames / HasGap=true，即使有附件 | Inconclusive | 非 Pass；无直接判定证明 |
| DroppedEvents > 0 / artifact 未绑定 / SHA 错误 | TEST 判断照常 | 正式 RECORD 非 Pass |
| 老 RunStepFact 没有新的可选标签 | 旧事实保留 | 默认 null，不为历史生成假证明 |

上述测试位于 `HOST/tests/AutoTest.Application.Tests/RealTestRecordSessionTests.cs`；历史测试和其他专业项目未改动。测试中 `Signals` 和 `EvidenceFinalizer` 使用合成输入，仅用于证明结果判定与 SQLite/报告合同，绝不替代实体 ECU 实测。

## 5. 验证、下一门禁与精确责任

| 层级 | 本次状态 |
|---|---|
| GitHub 写入/源码回读/改动路径 | 完成 |
| 主要源码的词法级括号与冲突标记 | 静态检查通过，不是编译 |
| Windows Restore/Release Build | NOT_RUN |
| Windows Testing/Records/Application Tests | NOT_RUN |
| WPF 启动/真实 Desktop 接线 | NOT_RUN / NOT_WIRED |
| HOST 原始 CAN/DBC/DEVICE 持续采集和真实 IRealRunEvidenceFinalizer | NOT_WIRED |
| 用户现场真实 ECU 与正式报告完整验收 | NOT_VERIFIED |
| B-01 代码是否被 PM 正式接受 | 待 PM 回读本修复并结合 Windows 回执复审 |

Codex 应基于当前 Draft PR #65 **最终精确 HEAD** 在 Windows 执行 HOST Release Restore、Build，以及 TEST、RECORD、Application 三层测试。必须回报编译警告/错误、测试数、失败/跳过及原始日志。得到这些真实验证结果后，再由 PM 评估 B-01 是否解除阻断；HOST 接线与现场实测仍是独立门禁。

项目管理请勿因文档文字将 B-01 自动标记 RESOLVED；**代码修复待复审 / 非可合并**。

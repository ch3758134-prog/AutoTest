> **2026-11-19 后续状态更新（不覆盖本次历史发现）**：C-01 已由 PR #65 的 `655e1bfe0d95a4a885574450faa79fcf1cff49ef` 单行修复并经 GitHub 源码回读，**不再是静态阻断**。后续应转入 Windows Restore / Release Build / 三层专项测试，详见 [C-01 复核和 Codex 门禁移交](TEST-RECORD-PR65-C01-CLEARANCE-20261119.md)。PR 仍 Draft / 未批准合并，HOST 入口和 ECU 验收未完成。

# AutoTest｜TEST-RECORD PR #65 B-01 定点修复项目管理复审（2026-11-19）

**审查对象**：[PR #65](https://github.com/ch3758134-prog/AutoTest/pull/65)、[Issue #64](https://github.com/ch3758134-prog/AutoTest/issues/64)；`feature/test-record-backend-20261119@5ade1bd9e94d3240e9d931d43005caac5778f515`（目标 `HOST@ffb23f8c05ec4a64b82cd781db6357685d082c04`）。  
**上次否决基线**：`c6bc2de3b97f1511dd29d9e253d908ad563068fc`，项目管理上一版 [PR65 B01 技术审查](TEST-RECORD-PR65-TECHNICAL-REVIEW-20261119.md)。  
**本轮准确差异**：7 文件（4 后端 C#、2 测试 C#、1 技术报告），GitHub ahead=9、behind=0；PR 仍 Draft/Open、未合并，GitHub mergeable=true 只表示 Git 合并能力。  
**本轮审查状态**：`B01_CAUSAL_DESIGN_STATIC_ACCEPTED / CS_TEST_SYNTAX_BLOCKER_FOUND / CHANGES_REQUIRED / WINDOWS_BUILD_NOT_RUN / HOST_INTEGRATION_NOT_WIRED / FIELD_NOT_VERIFIED`。**未经修正及真实 Windows 构建测试，不批准 PR #65 合入 HOST，更不批准 main 或生产 PASS 门禁。**

## 一、B-01 根本缺陷的审查结论：源码机制已定点修复

旧实现用本 Run 中任意 Valid DBC 观测换取正式 PASS，允许 `Read(unrelated) + Expect(1==1)` 借证据通过。

新 `RealTestRecordSession`：

1. 在执行前通过 `PreparedTestPlan.RestoreDocument()` 构建稳定 `StepId → TestTargetMapping` 的冻结 DBC 判定目标，只为启用的 `AssertSignal` 和 `WaitUntil` 登记。
2. `StepCompletedAsync` 仅在**本 Test phase 的确已 Completed**、有自身有效/无 gap 的观测、非空 EvidenceRef、可信记录质量、Actual 与观测数值匹配、DBC Asset/Revision/SHA、Role、FrameId、SignalName、Unit 匹配时，标记 `JudgementEvidenceKind=DirectDbcSignal`；Read/Expect 不可产生标签。
3. `RunCompletedAsync` 将本次实际成功的断言从 `RealRunResult.Facts` 按 `StepOccurrenceId` 绑定到 RECORD 已持久化的唯一步骤记录，并复核 `StepId`、`CaseAttemptId`、Phase、Disposition、质量、时间、数值、来源、EvidenceRef 和附件归档状态；不再跨步骤借用证据。
4. 正式 PASS 条件另要求：Test phase **至少一个成功且有完整直接证明的断言**、无未获证明的其他成功断言、原始 capture 完整、DroppedEvents=0、没有孤立/损坏附件。无证明则 TEST 的 `Passed` 不被改写，但 RECORD `Unknown/Inconclusive + EVIDENCE_GATE_NOT_MET`。
5. `Read → Expect($variable)` 的变量 provenance 未实现，P0 明确 fail-closed（可能让真实合法流程无法正式放行，但**不造成误放行**）；后续若需求批准应独立设计结果因果传播，不在本次修复扩范围。
6. `RunStepFact` 标签结构验证、SQLite JSON 可选字段兼容、HTML/JSON/CSV 的证明标识公开都已编入源码；证明标签本身**不等于可信硬件来源或防伪签名**，最终原始采集真实性和映射仍属于 HOST/DEVICE 的独立门禁。

**裁定**：`B01_DESIGN_LOGIC_ACCEPTED_STATIC`：上次指出的“无关有效 Read 借证据使常量 Expect 获正式 PASS”的**判定逻辑漏洞在本次代码中已消除**。但不能因此推断代码可编译、测试已通过、生产报告已经上线。

## 二、本轮发现新阻断项 C-01：测试源码有确定性 C# 字符串词法错误

文件：`HOST/tests/AutoTest.Application.Tests/RealTestRecordSessionTests.cs` **约第 600 行**，新 `UnrelatedValidReadDoesNotProveAnExpectJudgement` 测试尾部：

```csharp
Assert.DoesNotContain("\\"judgementEvidenceKind\\": \\"DirectDbcSignal\\"", json);
```

此处源文件中的双反斜杠 `\\` 与后续 `"` 使 C# 字符串过早闭合，`judgementEvidenceKind` 变为非法裸标识符；仅作括号/冲突标记的静态检查不会发现。经 GitHub **逐字符回读**确认该行含有连续两个反斜杠后立即接双引号，并非报告显示转义。属于**源码语法阻断**，无需假装 Windows 编译已经执行。

**要求 TEST-RECORD 专项只改此测试断言**，建议直接改为：

```csharp
Assert.DoesNotContain("DirectDbcSignal", json);
```

已有其他断言检查：原始 TEST Passed、正式 RECORD Unknown/Inconclusive、`EVIDENCE_GATE_NOT_MET`、Read 带有效观测但不带证明标签、Expect 不带观测。补上述断言可覆盖最终 JSON 未出现 DirectDbcSignal。修改后给出新的精确 PR HEAD。

**本项未修复前不应派 Codex 去大范围运行耗时测试**；先修这一行，再由 Windows Codex 在准确新 SHA 机械 Restore/Build/Test。

## 三、回归测试源码审查范围

已见到的用例：

- `EvidenceMustBeAttachedBeforeCommitToCountAsArchived`：AssertSignal 的合成观测+完整归档，预期 RECORD Completed/Pass；本测试非真实 CAN。
- `DirectWaitUntilIsEligibleWithItsOwnPinnedSignalAndArchivedEvidence`：WaitUntil 有直接证据，预期 PASS。
- `UnrelatedValidReadDoesNotProveAnExpectJudgement`：覆盖常量 Expect 与 Read→变量 Expect，预期 Unknown/Inconclusive。
- `DirectAssertSignalDoesNotLaunderAdditionalLiteralExpect`：有效直接断言 + 无关常量断言仍不可借证据正式 PASS。
- `MismatchedDbcSignalCanNeverBecomeCausalProof`、`LostCanFramesCannotBeMarkedAsCausalJudgementOrFormalPass`：DBC 映射异常、丢帧均不放行。
- 现存原始证据 Finalizer 异常、DroppedEvents、未归档、历史无证明标签和 SQLite/报告回读的用例仍在。

这些只是对提交的**测试意图与源码**的静态审查，`Windows Restore / Build / Test = NOT_RUN`；不可写成 7/7 或 PASS。

## 四、须保留的独立门禁

- **G-1 Windows 验证**：修复 C-01 后，Codex 锁定新 PR HEAD，进入 `HOST/` 运行 Restore、Release Build、三层专项 TEST/RECORD/Application.Tests；交真实编译错误、警告、测试数及日志。必要时运行相关 WPF 启动验证。GitHub 该新旧 HEAD 无成功状态检查；不自造 Build 结果。
- **G-2 HOST 实际接线**：`RealTestWorkbenchService` 尚未接入 `RealTestRecordSession`；`RunSnapshot`、扫码/Development 身份、Asset 指纹以及与唯一设备 Session/Fence 的关联必须由 HOST/PROJECT 以受控集成候选提供。
- **G-3 真实证据归档**：`IRealRunEvidenceFinalizer` 目前仅由合成单测实现；真实 CAN/DBC/DEVICE 全时段采集、时间/质量、drop/partial、fysnc 与 EvidenceRef 绑定和物理来源真实性尚未实测。完整性 SHA 不等于 DUT 因果证明。
- **G-4 现场验收**：用户现场以真实 ECU 运行有效 AssertSignal/WaitUntil PASS、判定 FAIL、无 CAN/错 DBC、丢帧、断连、取消/清理、循环多次并核查导出 HTML/JSON/CSV 与 SQLite/原始记录一致；所有不可用场景明确 UNKNOWN 或 ERROR，生产放行仍独立受控。

## 五、执行结论

**B-01 算法/因果证明设计已静态认可；当前精确 HEAD 仍因 C-01 确认的测试代码编译错误为 `CHANGES_REQUIRED`。** 仅申请 TEST-RECORD 修一行测试，再请 Codex 真实编译三层；无须重新研究 B-01 方案或重新编写核心数据模型。保留 PR #65 Draft、Issue #64 Open。PM 仅写 `Project_Management`，不直接越权修改专项源码或合并 `HOST/main`。

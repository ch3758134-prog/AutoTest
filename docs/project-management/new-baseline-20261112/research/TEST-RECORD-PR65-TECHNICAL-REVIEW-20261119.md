# AutoTest｜TEST-RECORD Draft PR #65 项目管理技术审查（2026-11-19）

**审查身份**：项目管理 / 技术架构审查，不代替 Windows 编译或 ECU 实测。  
**目标**：[PR #65](https://github.com/ch3758134-prog/AutoTest/pull/65) / [Issue #64](https://github.com/ch3758134-prog/AutoTest/issues/64)。  
**精确代码**：`feature/test-record-backend-20261119@c6bc2de3b97f1511dd29d9e253d908ad563068fc`，目标 `HOST@ffb23f8c05ec4a64b82cd781db6357685d082c04`，20 changed files，+2699/-34；Draft、Open、未合并。  
**审查结论**：`TR-00/01_DESIGN_ACCEPTED_WITH_LIMITATIONS / TR-02_SOURCE_CHANGES_REQUIRED / HOST_INTEGRATION_BLOCKED / WINDOWS_BUILD_NOT_VERIFIED / FIELD_NOT_VERIFIED`。**不批准 PR #65 当前 HEAD 合并 HOST，更不得接入 main；不是项目正式 TEST 报告闭环通过**。

## 1. 已认可的实现方向和可复用成果

1. 不复制 TEST DSL/解释器/RECORD SQLite；在 TEST 中附加 `result.*`、`TestReportSettings`，既有测试 JSON 缺省可读，`PlanCompiler` 运行前校验。
2. `IRealRunEventSink` 在步骤开始、完成和运行结束由实际 `RealRunCoordinator` 等待回调；`StepOccurrenceId`、父节点、Phase、LoopIteration、起止时间及采集事实建立了基础身份。
3. `RealTestRecordSession` 复用 `RunRecordWriter` / `RunReportExporter`，先 Begin durable 再接触步骤执行，逐步追加 `RunFact` / `RunStepFact`；RUN_BEGIN、TEST_CONTEXT、STEP_BEGIN、STEP_END、RUN_TERMINAL 保持分离。
4. 报告 HTML/JSON/CSV 从现有 RECORD 快照导出；失败、取消、无数据和证据不完整不直接冒充正式 PASS；保留既有源码/API 并增加兼容字段。
5. 合成数据专项测试源码已提交，**仅作为代码意图/回归输入，未运行也不能当真实 ECU 证据**。

以上是**方向/源码设计的审查接收**，不构成运行可用性或生产出厂批准。

## 2. 阻断项 B-01：已确认的正式 PASS 错判通道（必须在专项修复）

**位置**：
- `HOST/src/AutoTest.Testing/Runtime/RealRunCoordinator.cs`：`Expect` 接受纯常量比较，成功时置 `asserted=true`，因此可令 `RealTestVerdict.Passed`；
- `HOST/src/AutoTest.Application/TestRecord/RealTestRecordSession.cs` 的 `RunCompletedAsync`，约 314–337 行：`trustedSignal = StepFacts.Any(valid signal + evidence)`，`formalPassEligible = capture complete && zero drop && no orphan/corrupt && any evidence && trustedSignal`。

两者**并未确认可信信号所对应的 StepOccurrence 参与了产生 PASS 的实际判定**。构造可复核路径：

```text
Read(合法但与测试目标无关的 DBC 信号) -> 产生 Valid + EvidenceRef
Expect(left="1", op="==", right="1") -> TEST Passed
EvidenceFinalizer -> 对 Read 的 EvidenceRef 实际归档，RecordingComplete=true，Dropped=0
 => trustedSignal=true，formalPassEligible=true
 => RECORD 可能写 Completed/Pass，即使没有任何对 DUT 目标状态的判定
```

当前测试仅验证「孤立常量 Expect、没有可信证据」不能 PASS，未覆盖**“存在无关有效信号作为旁证”**这个混合用例。该问题是数据关联漏洞，不是缺少 UI 或 Windows Build 的推测；在问题关闭前 **不允许正式 Pass 语义合入 HOST**。

**要求**：P0 先限定适用范围为真实、经过配置和预检的 CAN/DBC 判定。对正式 PASS 必须存在至少一条明确的 ECU 判定 occurrence（例如 `AssertSignal` / `WaitUntil`），其合格观测值、Frozen DBC Mapping、证据和该判定 occurrence 的因果关联可追溯。对于 `Read -> Expect($variable)` 若准备支持，应将变量来源和观测 EvidenceRef 传播到判定 occurrence，而不能从本次 Run 中随便找一条别的观测；不支持时必须 fail-closed，不可默默放行。新增**合法正式 PASS、无关 Read + 常量 Expect、Read + 依赖变量 Expect、DBC 不匹配、丢帧**的正负回归测试。

## 3. 需要保留的集成门禁（不等于本专项可以越权修改）

**G-02 HOST 真实入口尚未接线**：`RealTestWorkbenchService` 没有调用新 `RealTestRecordSession`。本专项仅提供适配器，不能对外宣称“上位机自动运行已生成正式报告”。HOST 需在当前真实工程预检/单会话 Fence 下建立并冻结 `RunSnapshot`、`SessionIdentity`、RunMode、HostBuildIdentity；把真实执行路径接入唯一 TEST/RECORD，不再并行维护第二个权威结果。

**G-03 真实连续 CAN/DBC/DEVICE 采集与 Artifact 归档未实现**：`IRealRunEvidenceFinalizer` 只是 HOST 待实现合同。SQLite 证据 Hash 只保证**被提交的文件字节完整**，不能凭 Hash 自己证明这些字节来自真实 DUT、覆盖整段运行或与判定 occurrence 对应。HOST 必须提供收录起点、结束边界、时间同步质量、drop/partial、文件 fsync、`EvidenceRef` 的可信映射，并做断连、归档失败、重启恢复实测。明确 `RunEvidenceCaptureResult(RecordingComplete, DroppedEvents)` 语义；不完整时保留细节，不一概伪装正常采集。

**G-04 正式 PASS、生产统计边界**：**原则认可**“原 TEST Passed 但记录未完整 → RECORD Unknown / Inconclusive”，保留两者不同维度；不允许将该状态作为出厂 PASS。正式生产结果以记录质量与生产放行门禁共同决定，现有 Production/Scan 逻辑未获本专项授权更改。此 P0 暂仅为 CAN/DBC 判定链，未来 UDS/FLASH/WAVE 等不是一律必须包含 DBC 观测；需按获批准的步骤类别扩展可接受的 DUT 证据类型和证明方式。

**G-05 Windows Build/Test/真机**：`dotnet restore/build/test`、WPF 启动均 **NOT_RUN**，未确认新源码编译、反序列化、迁移/旧用例兼容、事件顺序、数据库 IO 或真实 CAN 运行表现。由 Codex 固定精确修订 HEAD 完成 Testing/Records/Application 构建与测试，返回真实错误与日志；不可用“括号匹配”代替。用户现场执行真实 ECU PASS/FAIL、NoFrame/Timeout、断连、循环、停止、恢复、报告与附件核对。

## 4. 非阻断但应在首轮验证中检查的质量风险

- `RunStepFact` 的 `StepOccurrenceId` 已附加到序列化 JSON，但 SQLite 目前按 `EventId` 去重，尚无数据库级重复 occurrence 防护；须说明重复事件、断连重入及重试的幂等策略。
- HTML、JSON、CSV 是三次独立文件原子替换，不是跨三文件事务；对报告部分导出失败，必须展示状态并允许依据原 Record 按 RunId 重新导出。
- 自动化测试当前提供的是合成端口/字节和 SQLite 合同测试，并非真实 ECU；请在报告样本中区分软件端口事实、观测质量与 DUT 判定。
- TEST 编辑器配置字段设计已入后端（`StepResultRecordingOptions` / `ReportSettings`），WPF Inspector 读取/保存与报告 UI 不是此次实现范围，须与 `ui/ide-design` 对齐、单独验收。

## 5. 下一次审查/交付

1. **TEST-RECORD 专项先修 B-01**，以最小增量的明确判定 provenance 规则和回归测试提交新 SHA，不回避 `Read+Expect` 用例；保留已经认可的数据/记录/报告架构，避免大规模推翻。
2. Windows Codex 在准确的新 SHA 完成 Restore/Build/Testing/Records/Application Tests 和 WPF 基本启动；原始日志入审查证据。
3. **HOST/PROJECT/DEVICE** 对 RunSnapshot、Identity、持续采集和完整 evidence finalizer 提交明确的接线候选及有限范围实测；任何新 DUT 写入/硬件控制不因本报告获授权。
4. PM 回读修订后的 B-01 + 构建证据，才评估 PR #65 合入 `HOST`。新 Fluent UI 能研究结果设置的体验，但不得预设“真实报告已经上线”。`main` 保持固定系统基线。

**最终结论**：研究与方向可继续，当前 PR #65 **CHANGES_REQUIRED / NOT_MERGE_APPROVED**。保留 Draft，Issue #64 不关闭，现场生产 PASS 门禁不放行。

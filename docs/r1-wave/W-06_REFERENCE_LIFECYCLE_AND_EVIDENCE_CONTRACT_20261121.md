# W-06｜参考波形生命周期、不可变证据链、TEST/RECORD 与报告合同 v0.2

**2026-11-21 / Issue #71**；源码`R1@e5a9e13ad522ddaf9092f44099f2ef26bdf1e19b`。本文件对 W-02 的 C# 草案做**产品级审核与接口差异补充**，不擅自修改 R1/RECORD 代码。状态 `REFERENCE_GOVERNANCE_PROPOSED / RECORD_CONTRACT_GAP_CONFIRMED / FIELD_NOT_VERIFIED`。

## 1. 先区分四种完全不同的对象

| 对象 | 定义 | 必须持久化 | 谁能判 PASS |
| --- | --- | --- | --- |
| `WaveCapture` | 某次 RUN 实际从 CAN/DBC 观察到的时间序列 | 原始新帧、样本与设备时间来源、窗口/结束原因、SHA | 不能，仅观察 |
| `WaveReferenceCandidate` | 为建立参考而**候选**的某次采集或导入 | 来源、拟用型号/信号/DBC、样本/质量、候选人、证据 ID | 不能 |
| `ApprovedWaveReference` | 经明确工程审核、适用范围和内容指纹冻结的参考 | Asset ID/Revision/SHA、批准/撤销/被替代状态、来源链/使用约束 | 本身不判 PASS，只是比较的已批准输入 |
| `WaveJudgement` | 本次实际曲线以确定的 Profile/算法作出的可复算判断 | Result、参数快照、质量分类、引用、差异区间、StepOccurrence、Report | 仅有充分原始证据和正式 TEST/RECORD 门禁时可能 PASS |

**规范来源与参考的区别**：参考曲线来自一个正常 ECU，并不自动证明“该曲线就是产品规格”。应区分 `SpecNormative`（明确设计/试验规格）和 `GoldenObserved`（批准的典型合格 ECU 实测曲线）。对后者默认只作形态对照；是否可作为 PASS 门槛需确认样本代表性、合格实体、重复性与容差。禁止仅选择一次“看着不错”的曲线就让所有后续 ECU 自动判合格。

## 2. 参考波形的正式生命周期

`Capture → CandidateDraft → EngineeringReview → Approved/Published → Superseded/Revoked/Retired`。

### 2.1 Capture：原始真实证据

除了现有 `WaveSeries`，应保存：
- `CaptureId, RunId?, CaseAttemptId?, CaptureStartOccurrenceId?, CaptureStopOccurrenceId?, CaptureGroupId, StationEpoch, CanSourceGeneration, CaptureArmed/Stopped, EndReason`；
- `PhysicalProvenance = RealUsbCanFrameObserved`，加上真实 `DeviceIdentity` / USB 固件版本与 CAN ECU Role；
- `SignalPin`：DBC asset Id/rev/SHA、CAN ID、Extended、Mux/value、Unit、原始 DBC bytes fingerprint；
- `ECU Context`：实际 DUT 编号或匿名受控追溯键、ECU 型号/硬件版本、已知 APP 固件/标定（不能推断的字段用 Unknown）、工位 TCF/TBC、CAN 速率实际读回、动作条件、测试负载、环境说明；
- `SourceQuality`：采样统计、Gap/Drop/Overflow、首末目标帧、报告更新周期/时间戳 provenance，所有拒绝原因；
- `SourceEvidenceRefs[]` → 原始 CAN 事件 EvidenceRef → 原始帧字节/CRC/时间/设备 epoch。不能只有曲线像某一帧。

`CaptureForRunAsync` 已有可选 RunId/StepId；但生产 `ConfiguredWavePort.Start/Stop` 不传这些值，`WaveEvidenceDocument` 也没有 `StepOccurrenceId/CaseAttemptId`。因此**必须以现实运行生命周期改接口**，不是仅在报告里填文本标签。

### 2.2 Candidate / Review：最小质量审核合同

- **不是**所有`WaveTimeQuality.CanJudge=true` 的曲线都可作生产参考：Replay/Simulation 在算法比较有用，但不得批准成 `PhysicalProductionReference`；
- Candidate 至少具备来源、适用 ECU 变型、已验证合格 DUT 的记录证明、原始证据、完整采集窗口、必须信号 DBC 版本、工程配置和统计质量；
- 审核人在**参考管理器**直观看到实际曲线、原始数据质量、使用条件、典型重复采样叠加；审核不是在测试程序里填写一个名字；
- `Approved` 保存明确的审核身份、UTC 时间、原因、审批依据，配置**只读**快照与 SHA；从已批准参考“修改”应该产生新 Revision 或新资产并写明 supersedes，不直接覆盖旧字节；
- 批准之后改变 reference signal、DBC、时窗、单位、阈值或适用 ECU 版本，需要**新工程审核**；
- `Revoked` 的版本禁止用于下一次新生产计划，历史报告仍能按旧内容哈希重建“当时使用了哪个参考”——但重新评价时须标出“现已撤销”。

### 2.3 当前源码明确缺口

- `WaveReferenceCatalog.Save` 每次 `AssetId.New(), Revision=1`，只避免同名/同信号再次存；虽然资产 SHA 不变，但**没有正式修订序列与审查状态**。建议支持 `ReferenceFamilyId + ReferenceAssetId/Revision/SHA + ApprovalRecord/Status`，不伪称“一个自动 Revision=2”已存在。
- `WaveReferenceWorkspaceService.FindCandidates` 与 `Promote` 只以相同 signal 和 `WaveTimeQuality.Describe(...).CanJudge` 允许；其中 Replay/Simulation 可以通过。现有 Replay 实例单元测试验证这一点，**不满足生产真实参考准入**。
- `WaveSettingsDocument.ReferenceName` 和 `ConfiguredWavePort.AnalyzeWithProfileAsync` 以**参考名称**在当前 catalogue 查找，若同名或资源变动会歧义/失效；R1 TEST 应锁定 `ReferenceRef (AssetId+Revision+SHA)`，名称只作显示。
- 正式禁止 `PromoteCapturedReference` 仅通过指定名字创建生产 Reference；工程采集候选与生产批准是两次不同动作。
- 参考中的`“导入文件”`应标明离线来源并保留原始校验，只有批准的物理来源链才能成为 `PhysicalProductionReference`；外部文件名和用户手工“这是实测”的文字不足以证明物理来源。

## 3. 必须考虑参考到底针对哪一种 ECU

配置边界建议：
- `Model + HardwareRevision`：两个硬件版本可能电机驱动、电流/负载或响应策略不同；默认不能共用同一条严苛 Reference。
- `Software/CalibrationRevision`：有软件算法/DBC 编码变化，旧 Reference 的物理值或时序语义可能不适用。
- `DbcPin + Signal Identity`：CAN ID、帧格式、Mux、信号名、物理单位、比例因子、偏置、无效值/Quality bits 必须一致。
- `Bench/Stimulus Variant`：IGN、供电条件、KEY 电阻网络/测试配置、夹具状态/负载特征对曲线有影响；存实际值，不强制虚构无法采到的环境量。
- `CaptureWindow/Trigger Semantic`：参考与实际必须对应相同动作语义、窗口长度和预采样约束，不应仅靠“都是 5s 曲线”匹配。
- `WaveProfileAlgorithmVersion`：即使参考字节没变，容差/插值/时间算法改变也会影响结果；正式运行计划要冻结判定 Profile/算法修订。

**最小 R1** 可只批准一种已核实 EPB 型号、一个 DBC 信号、一套明确动作/工位条件；扩展其它型号之前复核参考适用范围，不一开始做泛用汽车全部品类管理系统。

## 4. StepOccurrence 闭环：必须把四个不同事件连起来

正常行为：
1. `WaveStart` 本身有 `StepOccurrenceId_Start`；创建唯一 `CaptureSessionId`、写入完整 `RunId + CaseAttemptId + StationEpoch`，在 stream **已就绪**后开始记录，并保存 `ArmEvidence`。
2. TEST 中的 KEY/IGN/CAN/UDS 物理动作有独立 `StepOccurrenceId_Stimulus`；应通过显式 `TriggerReference` 关联 Capture，不应把`“捕获开始”`自动当作`“真实物理动作发生”`。
3. `WaveStop` 有 `StepOccurrenceId_Stop`；写入 `CaptureEvidenceId`、结束原因与完整窗口的质量结论。
4. `WaveCompare` 有 `StepOccurrenceId_Compare`；引用本次同一 `CaptureEvidenceId`、固定 `ReferenceRef` 和 `WaveComparisonProfilePin`，形成 `ComparisonEvidenceId` 和 verdict。
5. `RunCompleted` 由 `RECORD` 根据已存 StepFact 与归档附件校验*每个*真实 occurrence，包括循环/公共流程多次调用同一个 StepId，最后才允许 formal PASS。

有序证据图：
`RunId / CaseAttemptId → CaptureSessionId → WaveStartOccurrenceId → [StimulusOccurrenceId(s)] → WaveStopOccurrenceId → WaveCompareOccurrenceId → [CaptureEvidenceId, ComparisonEvidenceId] → [RawCAN EvidenceIds, ReferenceRef, Algorithm/ProfilePin] → RECORD Artifacts / Report`。

旧 `captureKey` 可以保留为 authored 的作用域名称，但**真实 runtime** 必须以 `RunId + current invocation path/loop iteration + CaptureSessionId` 作为唯一键，且每次 stop/compare 只能消费绑定的会话/证据；比较结果不能从上一轮循环或其它 case 拿到。

**接口时序注意**：Start 的时候未来的 Stop/Compare occurrence 尚未生成，不能要求 StartAsync 传未来 ID。建议 `Open(CaptureStartContext)` → `NoteStimulus(RealStepActionFact)` → `Close(CaptureStopContext)` → `Evaluate(CompareContext, FrozenReferencePin)`，后两步补齐关联；由 TEST 统一生成和授权这些上下文。这里是生命周期**增量适配接口**，不是新 TEST interpreter。

## 5. 原始 CAN 与样本真实性要闭合

目前 `RecordedRealRunEvidenceFinalizer` 会对 Wave JSON/Sidecar 的 SHA 校验、重新计算 comparator，并保存 `ECU_WAVE_VERIFIED_EVIDENCE`；原始 CAN timeline 有独立 ArtifactRepository。它已经拥有正确的持久化基础，**不应拆掉重新建库**。

但当前仍缺少独立证明：
- `WaveCapturedSample.SourceEvidence` 是否能在**同一 RunId** 的真实 CAN_RX 行中找到，且证据 payload 的 CAN ID、ECU Role、extended bit、DBC pin 映射一致；
- frame UTC + monotonic 是否落在经证据可证的 capture armed/stopped 窗口内，且属于相同 USB session epoch；
- 从 raw CAN 帧使用**当时冻结** DBC 的解码结果是否与 Wave sample 数值、单位、invalid flag、quality bits 一致，容许的浮点表示误差有确定规则；
- 比较的`actual waveform` 字节是否是这个刚刚归档的 capture，而不是另一个同名 `captureKey`；
- 参考 bytes 的 SHA/ID/Revision/Approved status 与 PreparedPlan/ComparisonEvidence 一致；
- 用户暂停、Stop、取消、订阅溢出或设备重连不能把半截实际波形当成完整参考或 PASS。

**精度/成本权衡**：不必在每次完整 WPF 报告打开时重复重解码几百万个 CAN 帧。正式运行写入时先完成一次原始帧→DBC→Wave samples 的证明及其**不可变证明摘要**，在报告打开时验证资产 hash/证明引用，必要时提供“重新核算”操作。若无法证明全部样本归属，正式 PASS 不可用；可以把不相关的辅助图表与裁剪数据标为“仅显示”。

## 6. RECORD 跨层阻断：最新 R1 仍能精确复现源码合同冲突

在 `Application/TestRecord/RealTestRecordSession.StepCompletedAsync`：
- 对通过的 `WaveCompare`，`waveRefs.Count==2` 时写 `JudgementEvidenceKind = "WaveComparisonPendingArchive"`。
- 而 `Records/Model/RunFact.cs > RunStepFact.Validate` 目前只接受 non-null `"DirectDbcSignal"`，要求 `SignalStatus="Valid"`、`SignalProvenance`；正式 Wave 证据不应伪造直接 DBC 单点的 Provenance。
- `RealTestRecordSession.RunCompletedAsync.HasArchivedWaveJudgementProof` 又必须读取精确字符串 `"WaveComparisonPendingArchive"`，证明现有**生产 WAVE PASS 写库前后的合同不一致**。例如 `RunStepFact.Validate` 会在正式 `AppendStepAsync` 时拒绝。

### 最小、严格修复建议

1. 新增区分良好的 `JudgementEvidenceKind` 值：`DirectDbcSignal` 与 `WaveComparison`（具体名称由 RECORD 审查统一）；验证器**按种类拆分**，不能将一切非空类型放行。
2. `WaveComparison` 的 StepFact 在**写入时**至少检查：`Run, CaseAttemptId, StepOccurrenceId, Phase=Test, Type=WaveCompare` 及状态 `Completed/Pass`，恰有两个非空互异 EvidenceRefs；它不能要求`单点 SignalProvenance`，而应有**独立** `WaveSignalPin, ReferencePin, ProfilePin, CaptureSessionId` 的结构化引用。
3. `PendingArchive` 是**暂时存储阶段状态**，不是直接 ECU 判定证据类型。允许先写 `WaveComparisonPendingArchive` 作为内部 staged 状态时，也必须有严格类型验证，归档后**在 durable 事务中**推进为 `VerifiedWaveComparison` 或追加独立的 `Verified judgement proof`，不能只在内存里改字符串、不能放宽 PASS 门槛。
4. `RunCompletedAsync` 的 formal PASS 依赖已归档`Capture + Comparison + 原始 CAN 逐样本证明 + ApprovedReference + ProfileVersion + OccurrenceLink`，而非「所有 ArtifactId 都存在」即可。
5. 归档失败/数据库中断/参考复查失败 → `INCONCLUSIVE/ERROR` + 避免错误产品放行；记录可以持久化失败状态，但不能先发布 PASS 再补附件。
6. `Expect`（普通变量/字面值）不能借 WaveAnalyze 的“比对结果字符串”为自己补一份 Wave 判定证据；保持真正的 `WaveCompare` 责任边界和现有 AssertSignal 的单点证据规则。

**必须有跨层测试**：强制创建一个 quality-checked Wave PASS with capture+comparison → `RealTestRecordSession.StepCompletedAsync` → `RunRecordWriter.AppendStepAsync` → `RecordDatabase.AppendStepAsync` → `RunCompletedAsync` → 导出 HTML/JSON → 重开并校验完整性；另构造 WaveRef 不在当前 Run 的原始 CAN 情况，必须拒绝 PASS。

## 7. 结果数据建议：既可给操作员看，也可给工程师审计

`WaveJudgementEvidenceV2`（概念 Schema，不是现有文件类型）：

| 字段组 | 至少应包含 |
| --- | --- |
| 运行标识 | `RunId, CaseAttemptId, StepOccurrenceId, StepId, ExecutionPath, LoopIteration` |
| 真实采集 | `CaptureSessionId, CaptureEvidenceId/Sha, Start/Stop/StimulusOccurrenceIds, StationEpoch, EndReason` |
| 信号来源 | `DbcAssetId/Revision/SHA, BusRole, CanId, IsExtended, SignalName, Unit, SignalSemantic, DeviceIdentity` |
| 时间与质量 | `ClockSource/Quality, TimestampSemantics, TriggerRef, WindowBounds, Observed/Valid/Rejected, Drop/Gaps, PeriodDistribution, MaxAge, CoveredDuration, QualityGateVerdict` |
| 判定 | `Method, AlgorithmVersion/SHA, ProfileAssetId/Revision/SHA, ExplicitThresholds, Interpolation, Alignment, Verdict, DiagnosticCodes` |
| 参考 | `ReferenceId/Revision/SHA, ReviewStatus, Applicability, ReferenceSourceCaptureId, RefQualityFingerprint`（功能规则可不需参考） |
| 偏差摘要 | `ValueError, IntervalViolations, MissingRanges, EventLatencyBounds, DwellEvidence, ReferenceCompareSummary` |
| 原始证据链接 | `SourceFrameEvidenceIds / compact verified coverage proof + RawCAN Timeline ArtifactId/SHA` |
| 报告 | `WaveResultArtifact, ChartPreview, DisplayUnits, HumanReadableReason, ReportNote, ExportedAt` |

不要把`用户能看到的蓝色曲线图片`当证据本体。WPF 现在 `WaveChartAnalysis.InWindow` 对展示用 downsampling 已尽量保留极值，这很好；无论如何比较器/报告门禁应始终使用原始 `WaveSeries` 和 CAN 事实链，不能使用图形投影点重新判定。

## 8. 清晰的波形报告结构（中文术语）

建议报告布局：
1. `波形判定：未通过 / 无法判断 / 系统错误 / 通过`，单独显示测试步骤执行状态。
2. `判定依据`：如“施加命令后 300ms 内至少收到 Applied 状态帧，后 1.5s 每个有效样本均为 Applied，持续监视间隙不超过已发布阈值”——具体数字必须来自用户已批准配置，不应硬编码。
3. `可见趋势`：目标/实际曲线、容差上下边界、触发点、质量缺失阴影、异常时间段；若没有 Reference 就只画规则/值域，不造黄金曲线。
4. `系统质量`：有效帧数、最长断档、时钟类别、是否有丢帧、可测精度提示。
5. `证据追溯`：Run/Occurrence，实际与 Reference 文件 SHA，算法配置 SHA，原始 CAN 原文引用、构建版本、设备状态。
6. `失败复查`：单击不满足的时段可定位原始 CAN/DBC 解码与 TEST 动作发生点，必要时导出一份不可变审查包。

**注意**：同名 Reference 不自动选择“最新”；生产历史中必须永久显示当时的确切 ID/Revision/SHA 与“当时批准状态”。已撤销参考不能变成被偷偷替换的新版。

## 9. 实施责任与保守交付

- `WAVE` 专项提议/实现 pure comparator + source provenance/capture reference validation/质量报告候选，先不扩大规模。
- `R1/TEST` 添加显式 capture lifecycle and action trigger contract，保留现有命令/执行器；
- `RECORD` 审核 `RunStepFact.Validate` 和正式 verdict/ArtifactRepository 的最小独立证明，不能在 Wave 项目分支擅自放开所有验证；
- `PROJECT` 仅存只读已发布 Reference/Rule 资产，不覆盖当前 DBC/信号版本；
- `UI` 用既有浅色专业工具和右侧 Inspector 展示，不新造另一套业务；
- `FIELD` 需实际 ECU + 主控/KEY/DBC/波特率/报告原始证据确认，模拟或 Replay 通过只表示算法逻辑无已知错。

**本研究与 W-02 为候审接口材料，不是编译通过的代码。**

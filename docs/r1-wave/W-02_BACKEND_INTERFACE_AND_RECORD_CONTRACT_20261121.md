# W-02｜R1 WAVE 后端接口、TEST 编辑属性、证据门禁与报告详细设计

**定位**：Issue #71 的**候审增量合同**，面向 R1 实施者。**只在研究分支留档，本文件不等于代码修改或验收通过**。源代码核对：`R1@fa3b18e948043a9d1960adddb627658fd40c8159`。禁止重建 `RealRunCoordinator`、DBC 解码器、`RecordDatabase`。现有 `IWaveSignalSource`、`SignalObservation`、`WaveEvidenceStore`、`RunStepFact` 和 `ArtifactRepository` 是首选扩展点。

## 0. 核心架构决策

1. **一个真实信号源**：保持 `LiveDbcSignalService`（现有 shared CAN→DBC 订阅）；WAVE 不另开 USB、CAN、DBC 和外部模拟 ADC。只允许增量记录来源、会话 epoch、真实帧 evidence、包接收质量。
2. **一个 TEST 程序模型**：沿用现有 `TestDocument` 和 `WaveStart/Stop/Analyze/Compare` 步骤类型。首轮增加强类型的判定请求/合同，由真实 `WaveCompare` 产生符合 RECORD 判定门禁的唯一正式 WAVE 断言；`WaveAnalyze` 保留观察/调试语义，不可用纯 `Expect` 字符串自行取得生产证据资格。
3. **一个 RECORD 数据库**：保留现有 `RealTestRecordSession + RunRecordWriter + ArtifactRepository + RunReportExporter`。Wave 捕获与比较 JSON 仍存不可变 `WaveEvidenceStore`，只追加严格标识和关联元数据/附件类型。
4. **不可自动批准参考**：参考资产内容 SHA/Revision 之外，要有明确来源分级、合格 ECU/工艺条件、审批事实；生产 TEST 运行前按准确 `ReferenceRef` 锁定，不依赖名称匹配“最新参考”。
5. **四态、独立质量**：`PASS / FAIL / INCONCLUSIVE / ERROR`。比较器输出与正式 Run PASS 两阶段门禁；第一阶段 PASS 并不等于第二阶段 PASS。

## 1. 当前合同与增量差异

| 现有接口 | R1 实际行为 | 必须的**最小增量** |
| --- | --- | --- |
| `ITestWavePort.StartAsync(target,captureKey,durationMs)` | 只有字符串 key 和持续时间，没有 Run/Occurrence 上下文 | 添加携带 `RunId + CaseAttemptId + StartOccurrenceId + SessionEpoch + TriggerPolicy + FrozenSignal` 的新调用；旧接口保留给非正式研究/兼容路径 |
| `StopAsync(captureKey)` | 取消采集后直接保存样本；缺真实完成原因 | 补 `StopOccurrenceId, StopReason, TargetWindowSatisfied, RawEvidenceWindow`，起止窗口不能只靠“有样本” |
| `AnalyzeWithProfileAsync(captureKey,profile,referenceKey)` | profile 校验了 DBC 但参考靠名称检索；不传 Run/Step | 补**冻结 ReferenceRef + Profile Revision/SHA**、`ComparisonOccurrenceId`、明确 judgement mode 和全配置 |
| `WaveComparisonConfig` | 8 数值字段 + RequireSynchronizedHardwareTime；默认可移位 50ms | 版本化规则、明确离散/连续、对齐/插值模式、特征窗口、时钟误差预算；正式默认不移位 |
| `WaveComparisonDetails` | Verdict/Code/Reason、Coverage/PassRatio/Shift/RMSE 等 | 扩充**质量统计、有效持续时长、p99 gap、首/末关键事件上下界、窗口违规、不可判定原因** |
| `WaveEvidenceDocument` | Capture/Comparison、可选 Run/Step，无 CaseAttempt/Occurrence | 增加上下文/触发/硬件会话/来源、实际原始帧引用、算法版本；兼容旧 Schema v1 为 LAB_ONLY |
| `RunStepFact.JudgementEvidenceKind` | 只有 `DirectDbcSignal` 被 Validate 许可 | 新的、独立的严格 `VerifiedWaveComparison` 证据类及验证条件；不把原有 DirectDbcSignal 稀释 |
| `RunReportExporter` | 通用 steps/facts/artifacts，没有专门的波形指标 | 从经过 VERIFIED 的 RECORD 附件派生波形报告部分，不增加新库和第二报告事实源 |

## 2. 建议强类型合同（C# API 草案；名称可以在 R1 实施审查时微调）

以下均为**接口伪代码**，本研究不改动现有源码，不能假称已编译。

```csharp
// AutoTest.Specialists/Wave: 正式生产请求的可验证上下文。
public sealed record WaveRunContext(
    RunId RunId,
    Guid CaseAttemptId,
    Guid CaptureStartOccurrenceId,
    Guid CaptureStopOccurrenceId,       // Stop 调用时才写入
    Guid CompareOccurrenceId,           // Compare 调用时才写入
    StepId CaptureStepId,
    StepId CompareStepId,
    string ExecutionPath,
    string PreparedPlanSha256,
    long StationEpoch);

public sealed record PinnedWaveSignal(
    SignalRef DbcSignal,                 // DBC AssetId / Revision / SHA256 + BusRole / FrameId / SignalName / Unit
    bool IsExtendedFrame,
    string DataSemantics);               // Discrete | Continuous | Enum; must be verified against DBC/requirements

public sealed record PinnedWaveReference(
    ReferenceRef Asset,                  // AssetId / Revision / SHA256, never only reference name
    string ReferenceName,                // display only
    string ApprovalRecordSha256,
    string IntendedEcuModel,
    string IntendedEcuHwRevision,
    string IntendedEcuFwRevision,
    string ProvenanceClass);             // PRODUCTION_REFERENCE | LAB_ONLY

public sealed record PinnedWaveProfile(
    AssetRef Asset,
    string AlgorithmId,
    string AlgorithmVersion,
    string CriteriaFingerprint,
    string CaptureSemantics);            // frozen with prepared test plan

public sealed record WaveTimingPolicy(
    string TriggerMode,                  // ExplicitObservedEvent | CaptureOrigin (non-response only)
    string TriggerEvidenceId,            // physical action/observed frame provenance, never USB ACK alone
    TimeSpan PreTrigger,
    TimeSpan AnalysisDuration,
    TimeSpan PostTrigger,
    TimeSpan MaximumClockUncertainty,
    TimeSpan MaximumSignalStaleness,
    bool RequireSynchronizedDeviceTime);

public sealed record WaveJudgementRequest(
    WaveRunContext Context,
    PinnedWaveSignal Signal,
    PinnedWaveReference Reference,
    PinnedWaveProfile Profile,
    WaveTimingPolicy Timing,
    string CaptureId,
    string ComparisonKind,               // DiscreteStateSequence | WindowBounds | AnalogEnvelope
    string ImmutableCriteriaJson);      // validated & canonicalized by typed schema in implementation

public sealed record WaveSampleQuality(
    string TimeSource, string TimeQuality, string ClockCalibrationId,
    long Observed, int Valid, int Rejected,
    long UsbPreDecodeDroppedDelta, long CanHubDroppedDelta, long SubscriberDroppedDelta,
    double MedianGapSeconds, double P99GapSeconds, double MaximumGapSeconds,
    double ValidDurationSeconds, double RequiredDurationSeconds,
    bool Overflow, bool MixedClock, bool Reconnect, bool HasUnprovenSource,
    bool WindowComplete);

public sealed record WaveJudgementResult(
    Verdict Verdict,                     // Pass / Fail / Inconclusive / Error
    string ReasonCode, string Explanation,
    WaveSampleQuality Quality,
    string AlgorithmId, string AlgorithmVersion,
    double? MaximumValueError,
    double? Rmse,
    double? ValidTimeCoverage,
    double? MaximumOutsideDuration,
    double? ResponseEarliestSeconds,
    double? ResponseLatestSeconds,
    EvidenceRef ActualCaptureEvidence,
    EvidenceRef ComparisonEvidence,
    ImmutableArray<EvidenceRef> RawCanEvidenceIds,
    string ReferenceSha256, string ProfileSha256);

// R1 TEST port: additive, not a second interpreter or second DBC signal feed.
public interface ITestWaveJudgementPort
{
    ValueTask<WaveCaptureStartReceipt> StartAsync(
        WaveRunContext context, PinnedWaveSignal signal, WaveTimingPolicy timing, CancellationToken ct);
    ValueTask<WaveCaptureReceipt> StopAsync(
        WaveRunContext context, WaveCaptureId captureId, CancellationToken ct);
    ValueTask<WaveJudgementResult> CompareAsync(
        WaveJudgementRequest request, CancellationToken ct);
}
```

注：`WaveRunContext` 中 `CaptureStopOccurrenceId`/`CompareOccurrenceId` 不能在捕获开始时猜测或伪造。实施应拆为阶段性不可变 receipt、在后续步骤由真实 `RealStepStarted.StepOccurrenceId` 补齐；上述伪代码是**一次完整捕获组的归档形态**，并非要求调用 StartAsync 时预知未来事件 ID。实现 DTO 可将对应字段改为可选并在状态转换时强制校验。

**不同 TEST 步骤拥有不同 StepOccurrenceId**：`WaveStart` 的开始事实、`WaveStop` 的结束事实、`WaveCompare` 的判定事实必须通过**同一个 Run/CaseAttempt/CaptureId + immutable parent links** 连接；不能假装这三个步骤具有相同 Occurrence。每次 Loop 有不同的 CaptureId，不允许仅用逻辑字符串 `captureKey` 在工位持久证据中去重。

## 3. 波形判定规则 Schema（候审 v2）

正式运行前，由 `WaveSettingsDocument` 的项目文件修订与 SHA 冻结下面类型的**规范化数值/时间字段**：

| 属性 | 说明 | 校验 |
| --- | --- | --- |
| `signal` | 被测映射名（选择器显示 DBC Bus/Frame/Signal/Unit/Rev），含连续/枚举语义 | DBC 精确全身份一致；绝不可字符串裸引用 |
| `referenceAsset` | 参考名供展示，但存 ID、Revision、SHA、Approval ID/SHA 与适用车型 | 禁止 RUN 时按同名最新自动替换 |
| `analysisProfileAsset` | 比较规则资源 Id/Revision/SHA + AlgorithmVersion | PreparedTestPlan/RunSnapshot 中冻结 |
| `captureKey / CaptureId` | 当前 TEST 程序逻辑键 + 运行实例 GUID | 跨 Loop/Case/Run 不混用 |
| `trigger.mode` | 明确观察触发、已记录硬件动作触发、无需事件的稳态窗口 | 响应延迟判定不允许自由平移/仅采用不可靠 ACK |
| `capture.preMs, durationMs, postMs` | 事前/触发/事后窗口 | 下界 >0（适用项），总时长不超现有 1h 安全限制 |
| `judgement.kind` | `DiscreteStateSequence`、`WindowBounds`、`AnalogEnvelope` | 必须与 DBC 数据语义兼容 |
| `interpolation.mode` | `None`、`ZeroOrderHold`、`LimitedLinear` | Discrete/Enum 禁止 LimitedLinear |
| `interpolation.maxGapMs` | 两点之间允许推算的最长时间 | 不大于经现场验证的正常刷新界限；跨 Gap/断连禁止 |
| `alignment.mode` | `NoShift`、`ExplicitTrigger` | 正式默认 NoShift；不得以 RMSE 优化移动响应 |
| `clock.maxUncertaintyMs` | 动作触发与 CAN 主机时间的最坏不确定度预算 | 需要来自已验证的标定记录；不可自行造值 |
| `coverage.minTimeRatio` | 有效时间/必要分析时间 | 由关键窗口分别检查，不靠纯采样点比率 |
| `coverage.maxUnobservedMs` | 单个最大不可观测片段 | 由 ECU 报文刷新率和工艺要求确定 |
| `coverage.requireStartAndEnd` | 起点和终点是否都必须具备样本支持 | 生产默认 true；不得用最后一点的异常时长 0 掩盖 |
| `value.absoluteTolerance / relativeTolerance` | 物理值差值与相对值 | 绝不全项目默认 5%；校验 finite/nonnegative |
| `segments[]` | `start/end`、`expected state/bounds`、`minHoldMs`、`maxDurationOutsideMs`、`edgeDeadlineMs` | 时间区段不可重叠冲突，超范围阻止 Prepare |
| `failurePolicy` | 数据质量缺失一律 `INCONCLUSIVE`；无效配置/参考一律 `ERROR` | 不允许配置“缺样本即 PASS” |

**旧 Config 迁移**：保留原 `WaveComparisonConfig` 与 Schema v1 的读/展示/回放能力，项目编辑迁移须显式确认并创建新的 v2 资源 revision/SHA；**旧的自动 50 ms shift 不得隐式沿用至生产 Profile**。`WaveCompare` 仍为现有 TEST DSL 类型；未来逐步提供单步编辑器**一键生成明确可见的 Start→Stop→Compare**，不另造隐藏执行 DSL。

## 4. TEST 编辑器行为合同（由 UI 专项展示，WAVE 只定义能力）

用户选择“添加步骤 → 波形判定”时，右侧属性检查区在**步骤列表先出现**，随后直接编辑，而非再开第二套属性窗口。字段组织建议：

1. **采集目标**：工程当前版本 DBC 信号（选择后同时显示 Bus/帧 ID/扩展帧/物理单位/信号名和资产 SHA）、采集持续时间、前后保护窗、触发来自前一个真实动作或一个收到的 CAN 信号条件。
2. **参考**：本工程该 ECU 型号/硬件/固件条件下**人工审核的参考**，显示名称和精确 ID/修订/SHA、录制时间、审批来源；无生产参考则不可将步骤发布为可执行生产版。
3. **判定**：状态顺序/达到时限/最短持续，或数值包络/稳态/超调和持续，最大缺失与时钟不确定度；仅展示与该信号类型有关的字段，工程阈值不自动猜测。
4. **结果和报告**：必填断言说明/业务要求 ID（若工程需要）、显示原始/参考曲线、有效覆盖、异常区间、原因码、回放证据、Run/Case/Occurrence；失效时允许直接跳转对应步骤配置。
5. **操作**：“运行此步骤”“运行此用例”“运行勾选范围”需符合 R1 统一 EXEC 路径。若单独执行 `WaveCompare` 缺先前 capture，则安全返回不可判/配置提示，不允许读取另一次旧 capture。

工具窗口可以独立进行**真实参考采集、审阅、命名审批与诊断分析**，但生产运行必须走 TEST 和同一个 RECORD；它不是第二个测试运行器。需要“采集参考”独立工作流时仍须共享 `LiveDbcSignalService` 和当前真实会话、经项目资源版本审批。

## 5. 判定算法建议结构（不改 DBC 模型）

`IWaveJudgementEngine.Compare(actual, pinnedReference, typedRules, observedTrigger, capturedQuality)` 分为：

- `GateInput`：核对 source 是生产真实记录、DBC Asset/Frame/Signal/Unit/Extended/Revision、同一个 capture session、reference Approval/Pinned SHA、捕获+比较 Occurrence 归属。若版本/文件损坏 → `ERROR`。
- `GateQuality`：源流 loss/overflow/reconnect/monotonic/signal decode、原始 CAN 可归档、时间戳类型、校准可用、起止窗覆盖、最大有效 gap、每个关键窗口有效覆盖不足 → `INCONCLUSIVE`。
- `ExtractEvents`：数字状态识别严格样本变化，生成转变时刻的可证明区间；数值曲线只有在特定算法+小时段内允许受限插值；保留空缺区间，不造零值。
- `EvaluateRules`：基于触发检查达到时限 + 保持，或窗口包络/稳定峰值/连续越差时长；时间不确定度和状态边界采用保守判据。
- `PersistComparison`：生成不可变 ComparisonEvidence，包含实际和参考捕获哈希/规则与算法版本、指标、异常区间/原因、原始 CAN 链，重新读取后校验一遍计算结果；作为 `WaveCompare` 返回给现有 TEST（不是私建 verdict）。
- `RecordGate`：在所有原始证据与结果文件完整归档、真实同 Run/Case/Occurrence 且比对 PASS 的前提下，申请 `VerifiedWaveComparison` 正式证据种类；失败的比对仍作为 FAILED 事实保存，不能伪装成 DirectDbcSignal。

**性能界限**：优先单信号每次不超过现有 `100000` 点上限；显式按文件体积/内存/样本容量拒绝超限、`INCONCLUSIVE/BUFFER_OVERFLOW`。比对算法为线性扫描或有限窗口与离散事件检查，不加无界 DTW、跨所有信号二次组合或 UI 线程阻塞。

## 6. RECORD 的精确独立证据类型与提交门禁

### 6.1 修复跨层阻断 P0-01

`RunStepFact.Validate()` 对当前 `DirectDbcSignal` 的分支维持原有约束；**添加独立、严格限定的 WAVE 种类**，不直接开放任意字符串。

候选正式字符串 `VerifiedWaveComparison`：
- 必须 `Phase=="Test"`，`Disposition=="Completed"`，`StepOccurrenceId` 与 `CaseAttemptId` 非空且唯一，Step 类型确为 `WaveCompare`，测量比较结论 `PASS`。
- 具备精确实际 CaptureEvidence 和 ComparisonEvidence 两个不可变附件（不能同 ID）；Comparison 指向该 capture 与固定 reference/profile/algorithm，采用原始样本复算相同结果。
- CaptureStart/Stop/Compare 的三个不同 Occurrence 必须链接至相同 `RunId/CaseAttemptId/CaptureId`；禁止复用上一轮的同名 captureKey。
- Reference 的来源必须 `PRODUCTION_REFERENCE` 且批准记录校验通过，DBC/Unit/ECU 型号及预设窗口一致；Replay/Simulation 仅可保留研究比较，**绝不能凭数学 PASS 获生产资格**。
- 每个实际采样的 `SourceEvidence` 能在当前 `RealRunEvidenceRecorder` 的原始 CAN 数据中找到同帧原始 payload 和同时间来源；检查所有 source lost delta 和窗口内存储完整性。
- `ArtifactRepository` 已封存原始 CAN timeline、波形 capture、reference/approval snapshot、comparison 和参数指纹。任何缺失、哈希不符、证据归属不匹配 → **正式 Run 不得 PASS**。

可用**两个阶段标签**实现：`WaveComparisonPendingArchive` 仅为内存中的运行中候选，不作为永久可授予资格的 `JudgementEvidenceKind`；归档后由正式记录门禁按不可变引用证明 `VerifiedWaveComparison`（如果当前表结构不允许事后修改 StepFacts，则在完成前使用不可授予 PASS 的候选类型并由 `RunCompletedAsync` 明确验证归档证据，且允许 Validate 该限定类型，输出报告仍标明 Pending vs Verified 的门禁事实）。**不能**把 `PendingArchive` 本身误解为“已证明”。

### 6.2 附件与跨步骤引用（现有 RECORD 增量）

建议附件 `kind`：
- `ECU_WAVE_CAPTURE_V2`：实际时间序列、原始 CAN EvidenceRef 序列、clock/epoch/time bounds、起止状态和样本质量；
- `ECU_WAVE_REFERENCE_PINNED`：固定 `ReferenceRef + Asset SHA`、审核记录、适用 DUT 型号/修订和来源；若引用不可复制的资源，应至少持久化签名快照/完整性可验证引用；
- `ECU_WAVE_COMPARISON_V2`：算法/配置版本、PASS/FAIL/INCONCLUSIVE/ERROR、逐段度量、异常区间及 capture-to-reference links；
- 现有 `R1_REAL_CAN_DBC_TIMELINE`：不可变完整运行帧时间线，按 SourceEvidence+StationEpoch+WindowTime 查回每个已用于波形的帧；
- 现有 `ECU_CAN_RX_DBC_SIGNAL`：单帧引用如需按 wave 标识复用则使用同一原始 ID，不复制造伪记录。

**数据关联图**：

`RunId → CaseAttemptId → (StartOccurrenceId → CaptureId → StopOccurrenceId → CompareOccurrenceId) → ActualEvidenceId → SourceEvidenceId[每个CAN样本] → 原始CAN_RX已归档文件`

另支：

`CompareOccurrenceId → ComparisonEvidenceId → (PinnedReferenceId/Revision/SHA, ApprovalSha, PinnedProfileId/Rev/SHA, AlgorithmVersion, TriggerEvidenceId)`

同一 Case 不同 Loop 生成不同 CaptureId；不同 Run 永远不共享当前 capture 证据，虽然**同一个已批准的参考资产**可合法复用。所有附件写入必须由现有 `ArtifactRepository` 完成并经过 `RunRecordWriter.AttachArtifactAsync` 关联到当前 Run，之后才允许终态提交。

### 6.3 报告内容与故障追溯

沿用现有 `RunReportExporter.ExportSummaryAsync/ExportFilesAsync`，只追加一个 “WAVE 波形判定”表节及 JSON 的 `waveJudgements[]`（从已封存的 StepFact+Artifacts 派生）：

- `RunId, CaseAttemptId, StepId, CaptureStartOccurrenceId, StopOccurrenceId, CompareOccurrenceId, DUT identity, Project snapshot, Station epoch`；
- `Signal/Bus/CanId/DBC Rev/SHA/Unit`、`Reference ID/Rev/SHA/审批记录`、`WaveProfile Rev/SHA/算法版本`；
- `Verdict/ReasonCode/Description`、完整输入时钟及质量（样本数、有效时长、gap、lost、windowComplete）、响应时间上下界、最大数值偏差、异常区间（起止时间和证据）；
- 实际、参考、比较附件的 `EvidenceRef, SHA, ArchiveIntegrity`，原始 CAN 时间线与相应帧可查；
- HTML 可叠加实际/参考和容限带/异常区间，但数据绘图是**派生展示**，不能参与判定或覆盖原始证据；
- 对 `INCONCLUSIVE/ERROR` 报告必须清楚说明不能断言 ECU 合格，而非仅空白图表或绿色图例。

`RunStepFact.AdditionalResultsJson` 可以放**界面可读摘要**，但结构化正式证据仍来自具有独立完整性验证的 ComparisonEvidence，防止超出 32768 字符或被显示 JSON 取代。

## 7. 最小实施的文件责任边界与推荐次序

| 次序 | 允许的最小目标文件 | 变更动作 | 不得修改 |
| --- | --- | --- | --- |
| P0-A | `Records/Model/RunFact.cs`、`Application/TestRecord/RealTestRecordSession.cs`、`Desktop/Testing/RecordedRealRunEvidenceFinalizer.cs` | 先修复 WAVE 判定类型 Validate/归档门禁冲突，严格限制 PASS 资格，补集成测试 | 不重写 RECORD schema/DB |
| P0-B | `Specialists/Wave/WaveTimeQuality.cs`、`WaveCaptureService.cs`、`WaveEvidenceCodec.cs`、`WaveReferenceCatalog.cs`（必要时新增小型 DTO 文件） | 来源/质量、Capture group + Occurrence、参考审批与固定引用、证据元数据（旧 Schema 明确 LAB_ONLY） | 不另建 CAN RX/DBC codec |
| P0-C | `Specialists/Wave/WaveComparator.cs`、`WaveComparisonConfig.cs`（优先新增 `WaveJudgementEngine` 类型） | 保留现有离线比较兼容，用**生产模式**补零阶状态、限窗连续值、不得自由对齐、时间不确定度 | 不把旧回放 PASS 作为生产结果 |
| P0-D | `Desktop/Testing/ConfiguredWavePort.cs`、`Testing/Runtime/RealRunCoordinator.cs`、`Testing/Authoring/StepTreeValidator.cs`、`Desktop/Projects/ProjectConfigurationDocuments.cs` | 最薄的强类型 V2 参数贯通、Profile/Reference 冻结、执行前预检、CaptureId 隔离 | 不复制 TEST DSL/runner |
| P0-E | `Records/Exports/RunReportExporter.cs` + 现有测试工程 | 添加 waveJudgements 从已验证归档生成；离线集成测试/真机采集测试 | 不新建第二报告库 |

**跨小组协调**：R1 正在修改同名 TEST/RECORD 文件。实施必须先核对 R1 最新 SHA/变更；WAVE 专项提供最小 diff/测试证据，由 R1 审查接入。`ui/ide-design` 负责界面布局、图标和 WPF XAML；本专项只冻结编辑字段及结果行为，不擅自布局大改。

## 8. 审查门槛与不自动决策项

- **须审查/批准**：V2 判定结构、生产参考来源/人工审批门槛、独立 WAVE RECORD evidence kind、正式默认禁止自动最优平移，以及可否把某个具体 ECU CAN 信号用于功能要求证明。
- **不要求用户现在给数字**：本专项不会自设 5%、50ms、更新周期、合格电流阈值或响应时间；请 R1 后续在实际 DBC 与工艺规范授权后填写。
- **需实物标定**：HostArrival 的时间不确定度、USB queue/WinUSB 到达节拍、ECU 真正报文周期、可观察边界和实际可用于触发的事件；不可能由源码编译得出。
- **必须有**：真实 PASS、受控真实偏差 FAIL、丢帧/错参考/错 DBC/时间不可证的非 PASS，以及同一 Run/Case/Occurrence/原始 CAN/报告回查。
- 未达到上述门槛时：`NO_PRODUCTION_PASS / FIELD_NOT_VERIFIED`。


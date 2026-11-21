# W-00｜R1 波形判定源码能力、物理采样和阻断项审查

**日期**：2026-11-21。**结论级别**：源码静态审查；未执行 Windows 编译、板卡烧录、真实 ECU 现场测试。**活跃 R1 快照**：`fa3b18e948043a9d1960adddb627658fd40c8159`，非任务创建时的旧 SHA。所有引用路径均在 `HOST/` 下，精确以该提交查看：[R1 源码树](https://github.com/ch3758134-prog/AutoTest/tree/fa3b18e948043a9d1960adddb627658fd40c8159/HOST)。

## 1. 物理信号与时间戳来源

现有生产管线：

`MK20 主控板 ECU CAN 端口 A1/A12 → USB 0x41 CAN RX indication → HardwareCanReceiveBridge → CanFrameHub → HardwareConnectionService.FrameObserved → LiveDbcSignalService → DbcLiveSignalDecoder/DecodedSignalPublisher → SignalObservation → WaveCaptureService/WaveComparator`。

- `Main_Control` 物理接口的 ECU CAN（对外 A12/H，A1/L）与 KEY CAN（B11/H，B1/L）必须区分。WAVE 只订阅 ECU CAN/DBC 信号，不添加未论证的 ADC 或示波器端口。
- `HardwareCanReceiveBridge.OnAsyncResponse` 在**主机收到 USB 异步包时**采集 `DateTimeOffset.UtcNow` 和 `Stopwatch.GetTimestamp()`。源码明确标注 `0x41` 不含主控硬件时间戳；`TimePoint.FromHostArrival` 标记 `HostArrivalOnly`。因此此时间不能直接表述为 ECU 事件发生的精确时刻，也不能证明微秒级或毫秒级延迟。
- `LiveDbcSignalService.OpenAsync` 为波形启动新的主机时间原点；获取的是来自共享接收流的事件样本，**不是**固定采样周期的模拟量采样。采样间隔由 ECU 报文实际发布周期、仲裁、USB/Windows 排队和应用负载共同影响，尚未通过现场测试标定。
- ECU 通过 CAN 上报的 MotorCurrent、Voltage 等数值是 **ECU 报告的物理量估计/内部值**；并非用独立探头在台架测得的引脚电压或电流。无报文时不得补造硬件读数；不允许用线性插值证明隐藏在两个报文之间的尖峰/脉冲存在或不存在。
- 当前硬件 ECU CAN 波特率（0x42/0x43）支持运行时配置且需要真实读回；它是 CAN 位速率，**不是信号采样频率**。每次现场记录实际读回值。
- 需要实测采集到的报文周期分布（min/median/p95/p99/max）、时钟抖动、丢帧、连续性、USB 队列溢出和同工位负载。没有这些测量值，任何“最低可判定响应时间”数字都是猜测。

## 2. 源码能力矩阵

| 领域 | 当前代码能力 / 精确位置 | 审查状态 | 主要不足与证据 |
| --- | --- | --- | --- |
| 单一真实 CAN 来源 | `Devices/Can/HardwareCanReceiveBridge.cs`、`CanFrameHub.cs`；`Desktop/Hardware/HardwareConnectionService.cs` | **已接线，未实测** | 0x41 不含设备时间戳；桥队列 4096、Hub 订阅 1024、监视分发与丢失事件需测量 |
| DBC 解码/信号绑定 | `Desktop/Hardware/LiveDbcSignalService.cs` + `Projects/Dbc/DbcLiveSignalDecoder.cs` | **已接线，未实测** | 校验 DBC ID/Rev/SHA/Signal/Unit；信号订阅渠道 1024，丢失/重连需逐次验证 |
| 信号时钟/采样质量 | `Specialists/Wave/WaveSampleBuffer.cs`、`WaveTimeQuality.cs` | **已有纯逻辑** | HostArrival/Replay/Simulation 均可满足“数据可比较”，无法单靠 CanJudge 证明是真实 DUT |
| 捕获和停止 | `Specialists/Wave/WaveCaptureService.cs` | **已有/已接线** | 定时/手动结束语义未区分；从实测接口调用 `StopAsync` 不传 Run/Step；无 StepOccurrence/trigger 针对性绑定 |
| 参考文件 | `WaveReferenceCatalog.cs`、`WaveReferenceRepository.cs`、`Configuration/WaveReferenceWorkspaceService.cs` | **已有/未达到生产参考审批** | 文件不可变/SHA 存在；仅名称选择，缺原始真实会话和合格 ECU 审核签认，重放数据可被提升 |
| 波形比较 | `WaveComparator.cs`、`WaveComparisonConfig.cs` | **有算法/有离线测试，真实误判风险未关闭** | 自动移位、离散值线性插值、采样点权重、边沿缺失、末端越差时长估计缺陷 |
| TEST DSL 执行 | `Testing/Runtime/RealRunCoordinator.cs`；`Authoring/StepDescriptorCatalog.cs`、`StepTreeValidator.cs` | **存在真实分支，未获真机验证** | WaveStart/Stop/Analyze/Compare 已有；缺按 occurrence 隔离 captureKey、冻结 ReferenceRef 和触发合同 |
| 正式 RECORD | `Application/TestRecord/RealTestRecordSession.cs`；`Records/Model/RunFact.cs`；`Records/Storage/RecordDatabase.cs` | **存在阻断冲突** | `WaveComparisonPendingArchive` 无法通过 `RunStepFact.Validate`；见 P0-01 |
| 附件归档 | `Desktop/Testing/RecordedRealRunEvidenceFinalizer.cs`、`RealRunEvidenceRecorder.cs` | **已有归档机制/需补精确证明** | 对波形 JSON 做摘要和复算，但原始 CAN 与每个 WaveSample、时窗、Occurrence 关联不足 |
| 报告 | `Records/Exports/RunReportExporter.cs` | **已有 HTML/JSON/CSV** | 有普通步骤/证据 ID/文件 SHA，缺独立 WAVE 比较摘要、极值/响应时间/异常区间和版本 |
| 工具视图 | `Desktop/Shell/WaveWorkbenchViewModel.cs`、`WaveWorkbenchWindow.xaml.cs` | **有只读证据浏览/画图** | 图形采样减少只用于展示；不是波形采集、阈值判定的独立执行引擎 |
| 测试覆盖 | `tests/AutoTest.Specialists.Tests/WaveComparatorTests.cs`、`WaveCompareContractTests.cs` 等 | **离线/合同验证** | Replay 仿真通过只能验证算法，不是 ECU 物理验收 |

## 3. P0 阻断及判定缺陷（按影响排序）

### P0-01：成功波形比对到 RECORD 时合同冲突（**阻断**）

`Application/TestRecord/RealTestRecordSession.StepCompletedAsync` 对成功的 `WaveCompare`（有两个证据 ID）写入 `JudgementEvidenceKind = "WaveComparisonPendingArchive"`；`Records/Model/RunFact.RunStepFact.Validate` 对非空类型只接受 `"DirectDbcSignal"`；`Records/Storage/RecordDatabase.AppendStepAsync` 入口立即 `fact.Validate()`。因此生产路径上的成功 WAVE 步骤记录**可能被直接抛错**。`RealTestRecordSession.RunCompletedAsync` 的 `HasArchivedWaveJudgementProof` 又明确期待 `WaveComparisonPendingArchive`，构成真正的跨层合同不一致。

**最小修复原则**：增设受限的 `VerifiedWaveComparison`（或经审查的精确命名）判定证据类型及其独立验证，不把它冒充直接 DBC 采样；测试必须证明从 StepStarted、StepCompleted 到已归档 WaveCompare 的 RECORD 提交和报告 PASS。不得直接去掉所有 `JudgementEvidenceKind` 校验。

### P0-02：生产参考源准入与修订引用未冻结（**假 PASS 风险**）

`WaveReferenceWorkspaceService.FindCandidates/Promote` 的过滤只以 `WaveTimeQuality.Describe(...).CanJudge` 与信号匹配为门槛；该方法也允许 `Replay/Simulation` 时钟对。现有 `WaveAnalysisConfigTests` 甚至明确使用 `ReplayDerived` 生成参考并验证提升成功。当前没有强制“真实 WinUSB 来源 + 经过核验的合格 ECU + 人工审核 + 冻结引用”生产准入门禁。`ConfiguredWavePort.AnalyzeWithProfileAsync` 仅通过参考**名称**从目录选取 ReferenceRef；虽文件本身包含 ID/Revision/SHA，但 TEST 未冻结该身份。

### P0-03：时间对齐可掩盖真实迟滞（**假 PASS 风险**）

`WaveComparisonConfig.FromContract` 默认 `MaximumShiftSeconds=0.050`（50 ms）；`WaveComparator.SelectAlignment` 用 RMSE 最小化搜索实际与参考的位移。若 ECU 响应延迟本身是检验目标，允许移位会把延迟直接“校正”掉。触发点应由 TEST 的已记录动作/被观察事件定义，允许的采样时钟不确定度不得当成 ECU 迟滞自由度。**生产默认不自动移位**，需要另设明确的采集/触发时间不确定度限制。

### P0-04：阶跃/数字状态线性插值以及按点通过率（**双向误判**）

`WaveComparator.Interpolate` 对所有信号实施线性插值；Boolean/Enum 等 ECU 状态使用该方法，会制造不存在的中间状态。`PassRatio = passed/compared` 与 `Coverage = compared/referencePoints` 不是时间权重：不均匀采样可偏向密集段，忽略短但重要的异常。`OutsideDuration` 以当前异常采样点到下一参考采样点时差累加，而末端异常点贡献 0；潜在低估尾部故障。需在正式算法中增加信号类型、最大保持时间、连续时间窗/关键边沿、末端最小保持/质量要求。

### P0-05：实际捕获的 Run/Occurrence/Raw CAN 关联不闭合（**证据门禁风险**）

`ConfiguredWavePort.Start/Stop/Analyze` 仅使用 `captureKey` 字符串管理当前会话，并没有传入 `RunId/StepOccurrenceId/CaseAttemptId`；`WaveEvidenceDocument` 只有可选 `RunId/StepId` 而没有 StepOccurrenceId。重复 Loop 中同 `captureKey` 会因 `_completed.ContainsKey` 拒绝复用。`RecordedRealRunEvidenceFinalizer` 可读到 Wave JSON、检验 SHA 和重算结果，且总原始时间线也会归档；但是“WaveCapturedSample.SourceEvidence → 该次真实 CAN_RX 字节 → 当前 Run/Occurrence 捕获窗口”的逐样本链没有强制核对。全局 `ObservedFrames > 0` 不能证明某一条波形确为真实有效采集。

### P0-06：采集质量 fail-closed 还需现场标定

`WaveTimeQuality` 对显性 Gap、Overflow、时间倒退、未知时钟返回不可判定是正确方向，但 `HardwareCanReceiveBridge.DroppedBeforeDecode` 是累计量，`CanFrameHub` 订阅 `Dropped` 也会进入后续帧；需要验证丢失计数递增差分、当前捕获窗口的归属及一旦恢复是否有正常数据质量恢复路径（避免错误延续成永久 INCONCLUSIVE）。在抓取不到目标报文、窗口过早停止、已过期 Latest 值、订阅途中的 CPU 突刺、同时运行 FLASH/工具等条件下均不得 PASS。

## 4. 物理能力准入分级（候选）

| 类别 | P0 是否建议以当前硬件开展 | 判据 |
| --- | --- | --- |
| CAN 布尔/枚举状态变更、达到/保持条件 | **可以做有条件的现场试验** | 基于真实帧和明示 trigger，零阶保持/有限 maxAge；时间门槛必须显著高于实测不确定度 |
| CAN 上报数值随时间变化、稳态/平台/超限时间 | **可以做有条件的现场试验** | 确认报文频率和抖动、有效时间覆盖、DBC 物理单位、信号状态位；不得声称外部探头测量 |
| ECU 响应延迟/上升下降沿精确时刻 | **先现场测时再决定是否可判** | USB 主机时间非 ECU 事件时间；要求捕获 trigger 的真实事件戳、不确定度预算和保守限值 |
| 电机相电流快速尖峰、PWM、引脚毫伏波形、纳秒/微秒脉冲 | **当前不支持** | 没有合格外部采样通道或 MCU 同步 ADC/硬件时间戳，严禁以 CAN 信号形状冒充真实电气波形 |

## 5. 基线和冲突管理

- START_HERE 与 Issue #71 引用 R1 原始 `94e82edd...`，本次回读活跃 `R1@fa3b18e...`。实施前按文件 diff 审核其后所有 R1 变更；本文件不把研究分支自动合并到 R1。
- 现有 `G3-WAVE-01-v0.1` 为历史设计，不替代当前代码；其“WaveAnalyze + Expect”建议不能自动视作当前生产 PASS 路径，因为现在 RECORD 门禁只验证 `AssertSignal/WaitUntil/WaveCompare`，`Expect` 不是直接 ECU 判定证据。
- 本调查不修改设备固件、TEST 运行引擎、DBC 解码器或 RECORD 数据库；W-02 将描述**增量接口和审查所需文件**。
- 静态结论的可靠程度：代码行为逻辑为**已回读**；真实 CPU/USB 抖动、丢帧阈值、ECU 反应波形为**尚无测量证据**。

## 6. 复审核对清单

- 精确 GitHub 路径：[WaveComparator](https://github.com/ch3758134-prog/AutoTest/blob/fa3b18e948043a9d1960adddb627658fd40c8159/HOST/src/AutoTest.Specialists/Wave/WaveComparator.cs)、[WaveTimeQuality](https://github.com/ch3758134-prog/AutoTest/blob/fa3b18e948043a9d1960adddb627658fd40c8159/HOST/src/AutoTest.Specialists/Wave/WaveTimeQuality.cs)、[ConfiguredWavePort](https://github.com/ch3758134-prog/AutoTest/blob/fa3b18e948043a9d1960adddb627658fd40c8159/HOST/src/AutoTest.Desktop/Testing/ConfiguredWavePort.cs)。
- 关键 Record 路径：[RealTestRecordSession](https://github.com/ch3758134-prog/AutoTest/blob/fa3b18e948043a9d1960adddb627658fd40c8159/HOST/src/AutoTest.Application/TestRecord/RealTestRecordSession.cs)、[RunFact](https://github.com/ch3758134-prog/AutoTest/blob/fa3b18e948043a9d1960adddb627658fd40c8159/HOST/src/AutoTest.Records/Model/RunFact.cs)、[RecordDatabase](https://github.com/ch3758134-prog/AutoTest/blob/fa3b18e948043a9d1960adddb627658fd40c8159/HOST/src/AutoTest.Records/Storage/RecordDatabase.cs)。

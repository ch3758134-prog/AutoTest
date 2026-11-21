# W-07｜从产品研究到首版可实测 WAVE 的工程切片、验收与准出规则

**2026-11-21 / Issue #71**；对照`R1@e5a9e13ad522ddaf9092f44099f2ef26bdf1e19b`。**状态：PLAN_PROPOSED / R1_GATE_PENDING / BUILD_NOT_RUN / ECU_NOT_TESTED**。

本报告是 W-04 的产品结构、W-05 的时序数学、W-06 的参考/RECORD 证据与 W-02/W-03 既有 API/测试计划的**实施排序**，不是授权动 main/R1/固件，也不是当前实现声明。

## 1. MVP 首版明确要交付什么

用户在真实 Windows ECU 工位中最终应能够：

`打开上次工程 → 选择已映射的实际 DBC 信号 → 真实波形捕获 → 采集质量可见 → 以一条真实合格波形生成候选并经过明确批准 → 在现有 TEST 步骤右侧 Inspector 设置判定方法/窗口/参考与版本 → 执行一次真实 KEY/IGN/CAN 动作 → 收到 ECU CAN/DBC 新帧 → WAVE 判断 PASS/FAIL/INCONCLUSIVE/ERROR → 在 TEST 运行结果和报告内打开实际、参考、偏差和原始 CAN 证据`。

**范围冻结 P0**：
- **必须有**：至少一条真实 ECU CAN/DBC 状态信号（推荐 EPB 已经实测的状态信号，实际 DBC 名称以工程为准）；捕获与质量、明确的判定窗口、简单状态时序或数值范围、一条明确批准的 Reference（如启用相似度）、跨层 RECORD 完整写库、HTML 报告查看。
- **必须拒绝**：参考损坏、Reference来自 Replay 被冒充真实、源 DBC/型号不一致、时间质量不足、目标报文缺失/丢帧、跨 Run/Occurrence 错引用、重复循环引用、报告归档失败。
- **不作为 P0**：物理模拟电流高速瞬态/示波器能力、自动多通道硬件时间同步、统计训练得“黄金包络”、复杂 FFT/DTW/机器学习模型、MF4 编码、独立新 TEST 编辑器/报告数据库。

## 2. R1 实施顺序（每一切片可审查、可回退）

| 批次 | 最小工作 | 精确代码责任点 | 合格证明 |
| --- | --- | --- | --- |
| `W-R1-P0-A` | 修复 WaveCompare PASS 无法持久化的跨层合同；保留 DirectDbcSignal 严格门禁 | `Application/TestRecord/RealTestRecordSession.cs`；`Records/Model/RunFact.cs`；`Records/Storage/RecordDatabase.cs`；`RecordTests` | 两个真实 evidence ID 的 WaveCompare 能入库/重开；没有证据/不同 occurrence 必拒；Run PASS 仍需附件完整 |
| `W-R1-P0-B` | 采集源/时间/窗口质量签名；对一个 `SignalRef` 真正计数队列丢失、新帧新鲜度、Epoch | `Devices/Can/HardwareCanReceiveBridge`；`Desktop/Hardware/LiveDbcSignalService`；`Specialists/Wave/WaveCaptureService.cs / WaveTimeQuality.cs`；只加必要边界 | 单信号 3 种源状态、缺帧、断连/恢复/时钟异常、无全局历史计数污染测试 |
| `W-R1-P0-C` | Runtime capture session + StepOccurrence 绑定；定时/手动 Stop、loop、trigger 锚点 | `Testing/Runtime/RealRunCoordinator.cs`；`Desktop/Testing/ConfiguredWavePort.cs`；`WaveEvidenceCodec.cs` | 先预采后动作，连续同名 2 次捕获/公共流程分别成对关联；取消和设备异常保留可审证据但不能 PASS |
| `W-R1-P0-D` | 已批准真实 Reference Pin 与最小安全判断；分类处理状态/数值，禁隐式 shift | `Specialists/Wave/WaveComparator.cs` / `WaveComparisonConfig.cs` / ReferenceRepository/Catalog；`Desktop/Configuration/WaveReferenceWorkspaceService.cs`；`ProjectConfigurationDocuments.cs` | Reference ID/Rev/SHA/Approval 显式冻结；Replay不能当生产准入；差异/质量测试通过，不自动移位遮挡延迟 |
| `W-R1-P0-E` | 同一 raw CAN → wave samples → result → StepFact → Artifact 的正式签名/证据完整 | `Desktop/Testing/RealRunEvidenceRecorder.cs`、`RecordedRealRunEvidenceFinalizer.cs`；`Application/TestRecord/RealTestRecordSession.cs`；`Records/Exports/RunReportExporter.cs` | 原始证据样本追溯、故障或证明不完整无法正式 PASS；报告重开可验证 SHA |
| `W-R1-P0-F` | 右侧 TEST 属性与波形专业窗口的必要交互，不重做主 UI | UI 专项维护 `TestStepInspector` / `WaveWorkbenchWindow/ViewModel`；波形负责字段与判定文案合同 | 使用者无需编辑隐藏技术字段即可选择/配置/判定；不新增多层属性弹窗；窗口随主程序退出 |
| `W-R1-FIELD` | 用户真实主控+ECU 正反和缺失测试 | Windows 编译/真实设备运行/导出归档 | 真实 PASS、真实 FAIL、断线 INCONCLUSIVE、Reference 错版 ERROR、报告/原始 CAN 可复核 |

**依赖关系**：A + C 是合法正式 TEST 波形事实的最先阻断；B + D 是“判得是否可靠”；E 是“能否正式出厂记录”；F 是“能否成熟操作”。可以并行专业分支研究，但**任何一个没有通过，都不能宣布 WAVE 现场 PASS**。

## 3. 不要把复杂的研究文档全部变成 R1 UI 控件

### 工程操作最短路径

一个实际 TEST 步骤右侧 Inspector 主要显示：
1. `信号：EPB 状态（通过 DBC 搜索，示例名称）`；
2. `判定方式：进入目标状态并维持 / 数值范围 / 参考波形比较`；
3. `触发：本用例中已存在的动作步骤（或选定观察触发）`；
4. `窗口：开始/结束、响应上限/维持时长`；
5. `依据：目标值/上下界/已批准参考版本`；
6. `质量：时钟、最大缺口、必须的新帧及记录策略`；
7. `保存并运行`（在主 TEST 编辑/运行语义内，不另造 Wave 执行器）。

普通的波形采集和 WAVE 工具允许没有正式 Reference 的研发采集；只有正式的 `WaveCompare` 判定要求合法 Reference。在界面中区分`配置草稿已自动保存`与`已批准可用于生产`。

### 操作者运行页

显示：`当前动作 → 采集中（目标 CAN ID/新帧数） → 分析中（采样质量） → 判定+原因（完整证据已保存/未保存）`。报告之前的`已采集`不得使用与`PASS`相同的视觉语义。运行停止和窗口关闭必须取消/清理会话、释放订阅，不准在后台留下隐形捕获导致下一次错误。

## 4. 建议现场先执行一个“真实 ECU 可观察性标定”

这是做成产品而非演示的关键前置：
- 选择用户现场真实已测通的 EPB CAN 信号，在现有 DBC 中核实帧 ID/extended/scale/unit/enum、ECU app 状态；
- 在真实 KEY/IGN 动作前开启 signal recording，采集**至少多轮**正常切换及保持、重复读取原始 CAN/DBC；
- 记录报文间隔分布、接收队列丢失、Windows 时钟/同 session mono tick 和 WAVE 样本数；
- 比对动作发出时间、主机观察状态改变时间，**不伪称 ECU 物理转换发生时间**；
- 在一次运行中同时打开常用 CAN 日志/手动台架或周期报文（仅实际允许安全并存的组合），测是否出现 sample gap；
- 确定某个真实信号究竟属于 State、Counter、ReportedNumeric 或 EventLatch，明确设计要求是什么；
- 依据实际测量定义 `MaxAge/MaximumGap/Duration/AllowedClockUncertainty`。采样不够支撑某一时限就设该规则不可生产判定。

没有以上数据就保留`FIELD_NOT_VERIFIED`，不得“猜”一个响应时间门槛。

## 5. 核心现场验收矩阵（需用户实际逐项操作）

| 用例 | 真实操作与设置 | 预期判定/证据 | 不允许出现的错误 |
| --- | --- | --- | --- |
| W-FIELD-01 | 真实主控连接 ECU；已批准符合当前 DBC 的参考或状态规则，执行正常 EPB 施加 | 合格且质量可证才 `PASS`，Run/StepOccurrence/原始 CAN/波形与报告闭环 | 只有 CAN 收到一帧就 PASS |
| W-FIELD-02 | 真实 ECU 释放/不同状态/故意使目标条件不满足；采样完整 | `FAIL`，明确失败状态/持续时间、时窗和原始帧 | 静默移位/插值使其假 PASS |
| W-FIELD-03 | 运行中拔 USB 或使真实目标报文丢失 | `INCONCLUSIVE` 或设备 ERROR，原始已有记录保留 | 断线后沿用最后状态 PASS |
| W-FIELD-04 | 引用另一个型号/旧 DBC SHA 或修改已批准参考字节 | 阻断 `ERROR`，报告说明目标资产版本 | 自动替换“最新”参考 |
| W-FIELD-05 | 把 Replay/Simulation 的记录尝试“发布”为生产参考 | 被拒绝；开发离线算法仍可比较 | 冒充真实参考 |
| W-FIELD-06 | 执行 WaveStart → KEY → WaveStop → WaveCompare，同一 case 的循环重复两次 | 两组独立 capture/compare、独立 occurrence + source frame 范围 | 第二轮被 `captureKey already used` 阻断/拿第一轮证据 |
| W-FIELD-07 | 参考或采集时窗缺前缘、末端或有效数据样本明显太少 | `INCONCLUSIVE`、列出缺少的时间段 | 根据剩余两个点或相似度足够就 PASS |
| W-FIELD-08 | 正常运行后在主程序退出/重启再打开报告 | 原始证据与指纹、状态和结论一致 | 报告重开自动用新版 DBC 重新判断 |
| W-FIELD-09 | 修改受控波形 Profile、DBC 或 Reference 后运行已准备计划 | PREPARED_PLAN_INVALID / Stale 拒绝 | 使用旧准备结果写新资源 |
| W-FIELD-10 | 点击停止、窗口关闭或项目切换发生在采集期间 | 清理资源、保留应保留的中断证据，不能变成 PASS | 僵尸 Wave session / 漏订阅 / 无法关闭 |
| W-FIELD-11 | 目标 CAN 静默但总线仍有其它 ID 帧 | 当前信号缺帧明确 INCONCLUSIVE | 用全局 ObservedFrames >0 判可用 |
| W-FIELD-12 | 人工检查实际+参考+差异图并按异常时段跳转到 CAN 原始帧 | 值/时间/DBC/Step/原始证据完全一致 | UI 下采样点被用于重新判定 |

**备注**：真实 FAIL 的构造必须安全、可恢复，不以危险的电磁阀/ECU 过载制造硬件故障。首版优先通过合法 KEY/状态动作和适当已发布目标约束构造可观察偏差；禁止修改隐藏算法结果来假装“实体 FAIL”。

## 6. CI/静态测试不能省，但要明确其意义

### 数学/质量离线自动化

完整执行 W-05 的 W2-01～W2-28 对抗性向量，并至少加入：
- 相同 waveform/不同 DBC fingerprint 的严格拒绝；
- 同样 Reference name 与不同 AssetId 的不同版本拒绝；
- 同一 Run 内多次同 StepId/不同 StepOccurrence 的完整消费与归档；
- UTC jump / monotonic continuity 下的时差结果；
- buffer 扩容/满容量不造成“安静的”假合格；
- 20/50/100/125/250/500/800/1000 kbit/s 的配置解析**不是样本频率**；
- 正常报文/错误报文/长度/扩展帧/DBC Invalid Value 的分类；
- 报告附件顺序/原始字节 SHA/算法配置 SHA 的可复算验证。

### RECORD 强制集成用例

必须测试：
1. `WaveCompare Pass` 的 StepFact 严格写入；
2. `WaveCompare Fail` 保存证据但不变成 PASS；
3. `WaveCompare Inconclusive` 不被全局别的 AssertSignal 的有效样本搭便车；
4. `WaveAnalyze` 不单独晋升为 DUT PASS；
5. Capture 来源 RunId/StationEpoch/Reference SHA 与 Compare 不一致强制拒绝；
6. StepOccurrenceId 不存在/重复/不在 Test Phase 强制拒绝；
7. 实际 Wave sample 每个 SourceEvidence 必须在 Run 的原始帧证据内；
8. 缺任一附件/sidecar篡改/文件不可读取时正式结果不可以 PASS；
9. Loop 第 2 次 WaveCompare 不可重用第 1 次 Capture；
10. 报告导出失败不得在数据库之外生成“已通过”的虚假 UI 状态；
11. 重开 HTML/JSON 指纹验证；
12. 更换 Reference 后旧报告原样保留；
13. 非 WAVE 的 `DirectDbcSignal` 结果回归不受伤害；
14. 非法委派 `Expect` 的历史变量结果不能代替 WAVE;
15. UI 图表高负载/新修订切换时 Wave 数据源身份不变；
16. `NoFrame`/`LostFrames`/`Disconnected`/`Stale` 不同状态各有正确中文说明和恢复动作。

这些是**代码/合同测试**，不能当作 W-FIELD-01 的物理验收；必须独立记录 Windows `dotnet build/test` 实际日志与 SHA。

## 7. Gate（真实实施负责人必须出示什么）

### `G-WAVE-DESIGN`：研究审查
- W-00~W-07 所有设计决策已读；确认只要真实 CAN/DBC，没有示波器/ADC；明确 P0 首版目标的 ECU 型号与真实信号。
- REVIEW A：TEST 是否接受分相 Capture context 和动作触发标识；
- REVIEW B：RECORD 是否接受独立、受限的 WaveComparison 类型+归档后证据验证；
- REVIEW C：Project/WAVE Reference 的批准/Revision 模型与现有资产不冲突；
- REVIEW D：UI 是否用成熟操作路径而非手填字符串实现，不抢主程序窗口与资源树。

### `G-WAVE-SOURCE`：代码审查
- 代码在批准的非 main/R1 专项分支或受控 PR；修改文件清单明确；不同部门既有 DBC/TEST/RECORD 不再建立第二套模型；
- `P0-A~E` 的接口合同与测试行为符合，至少完成 W2-01～W2-28 和 RECORD 套件，无恶化主 TEST 真相源；
- Windows WPF Release/Debug Build 与 .NET tests 有真实执行日志，静态审查不是编译成功；
- 原始证据/比较/Report 只在完全验证后能正式 PASS。

### `G-WAVE-FIELD`：用户工位实测
- W-FIELD-01/02/03/04/06/08/11 最低必须跑通；其它场景应尽可能按对应风险验证；
- 保存真实 `FirmwareRevision, BuildSha, DbcPin, ReferencePin, WaveProfilePin, ECU identifier, StationSession, RunId, StepOccurrenceId`；
- 对当前 HostArrivalOnly 精度出具可信可观察范围，不能超出实测宣称；
- 真正打开运行报告，逐项核对 raw CAN/actual/ref/difference/归档 SHA；
- 即便静态所有 PASS，现场缺证据时最终状态仍是 `PASS_WITH_LIMITATIONS` 或 `NO_GO`，不得写“所有波形判定现场可用”。

## 8. 现场验收记录模板（不得代填成 PASS）

`
W-FIELD-XX:
  branch: research/r1-wave-judgement-20261120
  candidate_commit: NOT_RUN
  windows_build: NOT_RUN
  windows_tests: NOT_RUN
  station:
    main_controller_id: NOT_COLLECTED
    firmware_revision: NOT_COLLECTED
    ecu_can_bitrate_readback: NOT_COLLECTED
    ecu_dut_identifier: NOT_COLLECTED
  dbc_asset: { id: NOT_COLLECTED, revision: NOT_COLLECTED, sha256: NOT_COLLECTED }
  wave_profile: { id: NOT_COLLECTED, revision: NOT_COLLECTED, sha256: NOT_COLLECTED }
  reference: { approved: NOT_VERIFIED, id: NOT_COLLECTED, sha256: NOT_COLLECTED }
  actual_run_id: NOT_RUN
  step_occurrence_id: NOT_RUN
  capture_session_id: NOT_RUN
  raw_can_artifact_sha256: NOT_RUN
  actual_wave_sha256: NOT_RUN
  comparison_artifact_sha256: NOT_RUN
  result: NOT_RUN
  operator_note: NONE
`

## 9. 建议后续管理决策

1. 批准 P0 首版 **`StateTransition + NumericEnvelope + 安全受限 ReferenceSimilarity`** 作为产品方向，首轮现场先单一真实 DBC 信号，按真实 ECU 能提供的数据选择具体场景；
2. **先修正 RECORD WaveCompare 证据种类冲突**，同时让 TEST/DEVICE 补强 capture 生命周期/时钟质量；
3. 让 UI 专项用 W-04 的操作路径实现轻量工具与 Inspector，勿要求先造复杂的完整波形脚本语言；
4. 实测之后再讨论更高精度物理探头/硬件时钟、多个通道关联、复杂特征匹配与标准文件互通。

**不得宣称**：本文件证明了真实采样精度、已接线 TEST/RECORD 正式波形 PASS、完成 Windows 测试、可量产放行。以上目前全部待审查或待现场实测。

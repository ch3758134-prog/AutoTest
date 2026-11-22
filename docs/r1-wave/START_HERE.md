# AutoTest｜R1 WAVE 波形判定研究专项接管入口

**身份**：WAVE 真实采样、参考波形与 TEST 判定算法专业 AI。  
**正式任务**：[Issue #71](https://github.com/ch3758134-prog/AutoTest/issues/71)。  
**研究分支**：`research/r1-wave-judgement-20261120`；初始基于 `R1@94e82eddb03e69b428fd4c63c2093a570d979c4e`。  
**状态**：`W-00~14 RESEARCHED / LATEST_PM_RECONCILED / DESIGN_PENDING_REVIEW / IMPLEMENTATION_NOT_STARTED / FIELD_NOT_VERIFIED`；最新源码回读 `R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f`。

## 本次最高优先级｜波形判定稳定性、算法、USB串口扫码证据归属（2026-11-21）

按用户最新补充，首先阅读：

- **[W-13 判定算法稳定性与实测资格](W-13_JUDGEMENT_STABILITY_ALGORITHM_QUALIFICATION_20261121.md)**：5 种稳定性，现有自动 50ms shift/状态插值/点覆盖的误判风险，状态/数值/参考独立算法，完整性先于 PASS，18 个反例与真实多轮正负/负载研究。
- **[W-14 串口扫码→Inspection→Flash→TEST→Wave 的身份证据合同](W-14_SCAN_TO_WAVE_TRACEABILITY_AND_RACE_PROTECTION_20261121.md)**：用户已明确实际扫码枪为 USB 虚拟串口/COM，非键盘模拟；SCANNER 负责真实 COM 输入/型号+唯一 SN/工程选择，WAVE 负责不拿旧 ECU/上次 Run/旧 Latest 波形给新 DUT 判定。扫码不是波形触发，正式波形采集在本次 Flash 成功的真实 TEST 动作之前预先就绪。

扫描的完整编码、USB 串口参数、设备状态机、跨工程检索由 SCAN 专项负责；本 WAVE 专项聚焦**采集/判定时序与身份来源完整性**，不建立第二扫码模块。现有 R1 `SerialBarcodeCapture` 只支持 `IVerifiedBarcodeLineSource` 抽象，未证明真 COM 接通。算法、串口与实 ECU 全部现场验收仍 `NOT_RUN`。

## 首先阅读｜现行产品合同已变更（2026-11-21 晚）

**现行权威**：[Project_Management/current 产品基线](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current/README.md)。其中 **专业工具、资源、TEST、Production 独立定位且共享底层**，WAVE 必须在**无工程时可打开并实时采集/分析多个真实 DBC 信号**。工程允许纳入参考，TEST 通过工程已冻结引用正式判定，生产直接调用全套 Enabled TEST。此前 W-03/W-04/W-07/W-09 有“当前工程是工具入口”和“单信号可作最终工具验收”的旧倾向，此部分被最新业务规则取代。

- **[W-11｜重新裁决旧研究与当前源码差异](W-11_LATEST_PRODUCT_BASELINE_RECONCILIATION_20261121.md)**：全量确认、保留、修订和旧设计取代。产品确认≠WAVE Schema 已批准。
- **[W-12｜真正独立的多轨工具、共享后端、正式判定与验收](W-12_INDEPENDENT_MULTITRACK_CAPTURE_AND_TEST_JUDGEMENT_CONTRACT_20261121.md)**：无工程工具/工具可选工程 DBC/多信号同屏与录制/参考纳入工程/TEST+RECORD 证据/现场负例。

**状态**：研究文件已落档；没有改 HOST/R1/main/固件；没有 Windows Build 或真实 ECU 数据准确度验收。现有 `WaveComparisonPendingArchive` 与 `RunStepFact.Validate` 仍存在合同阻断。

## 必须先完成的源码调查

主要回读：`HOST/src/AutoTest.Specialists/Wave/WaveComparator.cs`、`WaveComparisonConfig.cs`、`WaveCaptureService.cs`、`WaveTimeQuality.cs`、`WaveReferenceRepository.cs`、`WaveSharedStreamAdapter.cs`，以及 `HOST/src/AutoTest.Desktop/Testing/ConfiguredWavePort.cs`、`HOST/src/AutoTest.Desktop/Projects/ProjectConfigurationDocuments.cs`、`HOST/src/AutoTest.Testing/Runtime/RealRunCoordinator.cs`、`HOST/src/AutoTest.Application/TestRecord/`。

已有比较器会以绝对/相对误差、覆盖率、通过比率、最大位移、丢帧间隔、超容差时长作判定，**不能只因为有比较器就认为现场波形判定可靠**。需重点测试其在 ECU 波形阶跃、响应延迟、时间对齐、延时抖动、不均匀采样、短暂异常片段上的误判风险。对比纯物理模拟量波形与当前 USB/CAN/DBC 能获得的真实信号时间序列，明确硬件可测边界。

## 用户侧结果定义

打开工程→绑定真实 DBC 信号→采集并确认一个参考波形→保存参考及修订→设置判定窗口/容差/时间质量要求→在 TEST 测试步骤中引用→实时采集实际波形→判定 PASS/FAIL/INCONCLUSIVE/ERROR→记录 RunId/StepOccurrenceId/原始 CAN/实际与参考附件/算法参数和结果→正式报告。

明确区分“CAN 帧接收成功”“采集有数据”“波形质量足够”“波形判定通过”，不得把前两者等同于后两者。不要把所有波形容差默认到 5% 或固定时间位移，需要针对真实测试目标研究。

## 第一阶段必须提交

1. 完整源码能力矩阵及物理采样限制；
2. 正常/异常/边界波形判定的对照研究、真实测试设计；
3. TEST 属性合同、参考波形版本/采样质量/判定记录与报告的精确增量需求；
4. 只在明确审查通过后开发最小纵向闭环候选，提交精确差异与测试证据；不修改 main/R1/固件。

**边界**：不重写 DBC、TEST 执行器和 RECORD；UI/UX 研究持续独立，R1 接受通过审查的波形运行/报告接口。原始数据回放只能作为算法测试，**不能冒充真实 ECU 波形验收**。

## 2026-11-21｜第二轮成熟产品深度研究（已经落档，未实施）

- [W-04｜成熟工业产品对标与产品架构](W-04_INDUSTRIAL_PRODUCT_MODEL_AND_UX_RESEARCH_20261121.md)：NI TestStand、TraceTronic ecu.test trace.check、MathWorks Simulink Data Inspector、Vector CANalyzer、ASAM MDF，区分 CaptureAndInspect、StateTransition、NumericEnvelope、ReferenceSimilarity 四种用途，并给出 TEST Inspector、独立工具工作台及报告真实操作流程。
- [W-05｜时间可观测性、判定数学与 28 组对抗向量](W-05_TIME_QUALITY_AND_JUDGEMENT_SEMANTICS_20261121.md)：主机接收时间不能当作 ECU 事件时间，触发前订阅/源帧时窗证明、不同信号的 ZOH/线性策略、时间误差上界及 fail-closed 分类；修正自动 shift、离散线性插值、点覆盖率、尾端越差错误。
- [W-06｜参考波形治理与 TEST-RECORD 证据门禁](W-06_REFERENCE_LIFECYCLE_AND_EVIDENCE_CONTRACT_20261121.md)：Capture 候选→审核→发布/修订→撤销，明确 ReferenceId/Revision/SHA，联通 RunId/CaseAttemptId/Start/Stop/Compare 各个 StepOccurrence、raw CAN 与一致性报告；再次核对 WaveCompare 的 `WaveComparisonPendingArchive`/ `RunStepFact.Validate` 类型冲突。
- [W-07｜首版实施顺序与真机验收](W-07_PRODUCT_MVP_ROLLOUT_AND_ACCEPTANCE_GATE_20261121.md)：P0-A～F 切片、12 个现场 W-FIELD 场景、NOT_RUN 证据模板与 G-WAVE 准入门槛。

**实测边界**：现有 MCU/USB ECU CAN 接收采用 HostArrivalOnly；R1 没有 ECU 物理事件级同步时戳，也没有独立高速模拟传感器输入。Production WAVE PASS 需要真实源、正式已批准参考或明确功能规则、无未解释缺测、同一次 Run/Occurrence/raw CAN 关联以及 RECORD 正式完成，不能用算法 Replay PASS 或仅有漂亮曲线冒充。

**与本轮活跃 R1 的相容性**：最新 `R1@e5a9e13...` 比首轮 `fa3b18e...` 多 54 提交，新增了证据/绘图非阻塞等性能改动及 IDE/USB 安全维护，但 WAVE 主比较器和 WAVE 判定 RECORD 的核心合同问题仍然存在。合入之前必须重新看活跃 R1、完成 Windows Build/Test 与真实 ECU 验收，未授权前不改 R1/main/固件。

## 2026-11-21｜W-08 容差标定研究补充

- [W-08｜容差来源、测量系统重复性与参考资格研究](W-08_WAVE_LIMITS_MSA_AND_REFERENCE_QUALIFICATION_20261121.md)：成熟产品不能简单拿一台合格 ECU 的一次采样曲线和任意 ±5% 容差作为正式生产标准。应区分产品规格、合格样本间差异、重复测量波动、设备/时间采样不确定度；通过受控重复采集、适用型号核对、安全负例与阈值批准形成生产规则。此为候审方法论，`FIELD_NOT_VERIFIED / NO_CALIBRATION_DATA`，R1 不必先重建统计平台。

## 2026-11-21｜第三轮：独立波形工具与准确度保证

- [W-09｜独立波形专业工具与实时采集工作台](W-09_LIVE_WAVE_TOOL_CAPTURE_WORKBENCH_20261121.md)：明确当前 WAVE WPF 仅是读取已存证据的浏览器；需要独立真实 CAN/DBC 信号选择、LiveMonitor/采集/停止/触发/预采、质量可见、离散阶梯图、参考叠加、已存证据、UI 与记录优先级分离、主窗生命周期绑定。工具不能建立第二套 CAN/TEST 或代替正式执行器。
- [W-10｜波形准确度保障与现场资格验收合同](W-10_ACCURACY_ASSURANCE_AND_FIELD_QUALIFICATION_20261121.md)：以数值解码正确、采集完整、时间可信、判定可复算为四种不同准确度；八类验证事实、时间误差边界、现场校准策略、受条件限制的生产准出等级、18 个故障向量。实际 USB 0x41 为 HostArrivalOnly，不能宣称 ECU 物理高速信号精度。
- 本次新增研究仍为 `DOC_ONLY / DESIGN_PENDING_REVIEW / WINDOWS_NOT_RUN / FIELD_NOT_VERIFIED`，最近源码静态比较 `R1@8e6aaa556e70ce025a18c20abeb331fa812b055d`；不得未经审查改 R1/main/固件。同期旧 W-04~08 不取代此二份专门合同。

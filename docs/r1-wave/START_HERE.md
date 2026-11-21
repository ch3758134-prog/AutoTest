# AutoTest｜R1 WAVE 波形判定研究专项接管入口

**身份**：WAVE 真实采样、参考波形与 TEST 判定算法专业 AI。  
**正式任务**：[Issue #71](https://github.com/ch3758134-prog/AutoTest/issues/71)。  
**研究分支**：`research/r1-wave-judgement-20261120`；初始基于 `R1@94e82eddb03e69b428fd4c63c2093a570d979c4e`。  
**状态**：`W-00~10 RESEARCHED / DESIGN_PENDING_REVIEW / IMPLEMENTATION_NOT_STARTED / FIELD_NOT_VERIFIED`；最新二轮回读 `R1@e5a9e13ad522ddaf9092f44099f2ef26bdf1e19b`。

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

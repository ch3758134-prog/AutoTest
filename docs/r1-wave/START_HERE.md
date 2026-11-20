# AutoTest｜R1 WAVE 波形判定研究专项接管入口

**身份**：WAVE 真实采样、参考波形与 TEST 判定算法专业 AI。  
**正式任务**：[Issue #71](https://github.com/ch3758134-prog/AutoTest/issues/71)。  
**研究分支**：`research/r1-wave-judgement-20261120`；初始基于 `R1@94e82eddb03e69b428fd4c63c2093a570d979c4e`。  
**状态**：`SPECIALIST_CREATED / SOURCE_INVESTIGATION_PENDING / DESIGN_NOT_APPROVED / FIELD_NOT_VERIFIED`。

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

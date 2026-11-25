# AutoTest R1｜正式生产检测专项研究与整改

- 正式生产静态评分基线：63/100（R1 UI/UX 模块台账，原来从 48 分提升到 63 分）。
- 本次研究/设计成熟度：85/100（不是代码完成分，更不是生产验收分）。
- 分支：R1；main 维持受控。
- 状态：源码和回归测试已提交；Windows WPF、MSBuild、真实 USB/ECU、夹具、FLASH 均未验收；生产 Gate 继续 BLOCKED。

## 研究评分（要求先达 80 分）

| 领域 | 评分 | 主要依据 |
| --- | ---: | --- |
| 单工位工艺和流程 | 19/20 | 唯一部署工程、扫码、实体 START、前置 FLASH、全部 TEST、正式 Inspection |
| 身份与冻结的可追溯性 | 19/20 | 工程/资源 SHA、正式 TEST 引用版本、固件与安全算法、扫码 COM 和 USB 连接世代 |
| 安全及真实/开发隔离 | 19/20 | 任何扫码、USB ACK、软件 START、开发调试都不能假冒正式生产开始与 PASS |
| 信息架构和操作反馈 | 15/20 | 第一项生产阻断具体可见，TEST 顺序、IF 分支和信号判据直接展示 |
| 实施路径与验证反例 | 13/20 | 具备独立准入检查与回归合同；但实体 START、机械反馈及最终 RECORD 适配器缺真实来源 |
| **总计** | **85/100** | 方案研究超过 80 门槛，允许开展源码整改 |

## 关键源代码事实

- AutomaticTestHomeViewModel 目前不持有正式生产执行事实（HasFormalExecutionFacts 为 false）；已有工程整组运行是开发验证，不是正式生产。
- ProductionStartGate 是一次性实体上升沿的领域合同，但目前没有经确认的固件实体 START 输入与可信上报适配器。
- ProductionResultCoordinator 要求 IVerifiedProductionCommitSink 归档完整证据；通用 RECORD 的 UNKNOWN 不能当成已验收的生产 PASS。
- StationDeploymentStore/ProductionPreparationService 已核对发布工程、完整 TEST、固件与安全算法和工位动作的冻结指纹。GPIO 回读不是实际机械夹具到位证据。
- 原 AutomaticTestPlanService 按字母排序分支，显示 ELSE 在 THEN 前，对 IF/CAN 读取、信号判定等关键步骤缺真实参数概要。

## 实际整改

1. 正式生产 TEST 列表：THEN 在 ELSE 前；各分支中文标签；IF/READ/AssertSignal/WaitUntil/Expect/LOOP/FLASH/WaveCompare 有关键执行条件摘要；不显示敏感安全算法路径，也不伪造测量。
2. 单一正式准入投影 ProductionAdmissionReview：顺序检查发布工程、资源、USB、CAN 码率、COM 扫码、扫码冻结身份、工程同一版本、USB 会话、扫码端口、冻结 CAN 码率、实体 START、正式 Inspection 写入器。优先提示第一处阻断原因。
3. 每次被接受的真实扫码冻结 StationEpoch；连接断开重连/COM 改变/工程或波特率改变不得延续旧扫码；正式扫码时主控若未连接，直接拒绝。
4. 正式生产调用软件 START 一律拒绝；工位页面不复用开发运行结果，不显示无证据的正式 PASS。
5. 新增源代码测试：IF 顺序/可读判据、扫码不等于 START、会话漂移与端口漂移、实体 START 与正式 RECORD 的联合前置条件。

## 后续阻断与验收

P0 仍有实体 START 硬件输入及固件事件未实现；夹具物理到位缺实物反馈；正式单台 RunId 及 FLASH+TEST+清理+StepOccurrence 结果的一致、耐久归档未接入。以上不能凭静态判断放行。下一步应先确定硬件真实输入，然后基于 ProductionStartGate 连接一套正式 Inspection Orchestrator 和 VerifiedInspectionCommitSink，不可新建独立 TEST 执行器或 Mock。

Windows WPF 启动及自动连接不卡死、真实 COM 扫码、ECU CAN、实体夹具、烧录及 APP 后验、异常停止/断电/复检、报告数据完整性都需要现场测试。此次 85 研究分不修改已归档的 63 分产品静态基线，生产准出保持 BLOCKED。

参考：NI TestStand Process Models https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html
NI TestStand Process Model Architecture https://www.ni.com/docs/en-US/bundle/teststand/page/process-model-architecture.html
NI UUT Result Table Schema https://www.ni.com/docs/en-US/bundle/teststand/page/uut-result-table-schema.html

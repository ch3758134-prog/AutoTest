# W-13｜波形判定稳定性、算法选型与防误判实证研究 v0.1

> **日期** 2026-11-21；**任务** Issue #71；**最新源** `R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f`，WAVE 源码与上一检查版本一致；**当前现行要求** `Project_Management/docs/project-management/current`；**状态** `RESEARCH_PROPOSED / SOURCE_GAPS_STATICALLY_CONFIRMED / WINDOWS_NOT_RUN / ECU_NOT_CALIBRATED`。
>
> 本研究不把单条图形形状误称“符合 ECU 物理要求”，不强行将 CAN 上报的离散量插值为模拟量。WAVE 专项负责测量和比较算法；扫码型号/工位业务属 SCAN/Production，不在本文件增加扫码 Recipe。

## 1. 必须先明确定义“稳定”的五种不同概念

1. **真实信号稳定性**：正常同一 DUT 的状态、数值形态/响应时间是否在已批准工况下重复；如果 ECU 内在行为变化是真实问题，不能让算法将其全部平滑掉。
2. **捕获稳定性**：源 CAN 帧、DBC 解码、USB/Windows host timestamp、队列和录波磁盘在重复采样时有没有缺口、拥塞、来源混合与突变；图上稳定不等于源完整。
3. **算法确定性**：同一个不可变 `Actual+Reference+AlgorithmVersion+Rules`，重复离线计算必须得到相同的 Verdict/指标/异常区间/原因，算法不能读当前“最新 Reference”或随机初始化。
4. **边界鲁棒性**：同一真实合格 DUT 多轮观察在物理允许的变异范围内不应无原因 PASS/FAIL 抖动；但“用大容差保证全 PASS”并不等于可靠性。
5. **生产追溯稳定性**：扫码/型号、已冻结 DBC/TEST/Reference、Run/StepOccurrence、接收会话 epoch 和真实 raw CAN 必须一一对应；不能将上一台的最后有效值用于下一台 PASS。

以上层次分别验收，不能用一个“正确率 99%”覆盖。NIST/SEMATECH 对测量过程 Characterization 明确区别 repeatability/reproducibility/stability；其 conformity assessment 研究特别关注测量不确定性与误接收/误拒风险：
- https://www.nist.gov/publications/nistsematech-engineering-statistics-handbook-chapter-2-measurement-process
- https://www.nist.gov/publications/assessment-conformity-decision-rules-and-risk-analysis

## 2. 最新 R1 比较器的具体算法审计（准确源码而非想象）

`HOST/src/AutoTest.Specialists/Wave/WaveComparator.cs`：
- 第 `32-35` 行核对 SignalRef 和 SourceRevision，这一约束应保留。
- 第 `36-42` 行在 Profile 显式 RequireSynchronizedHardwareTime 时拒绝 HostArrival，方向正确，但当前普通分析仍用 HostArrival；不代表普通主机时间具有 ECU 电气毫秒精度。
- 第 `43-52` 行做输入质量和 median/gapFactor 的一致性校验，但未建立**动作触发前/后实际报告窗口的覆盖与物理事件可见性证明**；简单两点+median 不能表示可检测所有瞬态。
- 第 `54-69` 行通过 `SelectAlignment` + 采样点 Coverage/PassRatio + out-of-tolerance 时间判断。**错误风险**：高密集采样区占主导，一段低密度失效仍可漏判；Reference 时间窗末端缺失不一定有相应 penalization。
- `RelativeSeconds` 第 `74-75` 行基于 UTC delta 而非真实单调钟 tick 计算；系统 UTC 跳变应记录为 Quality error，不能悄悄用后处理对齐。
- `SelectAlignment` 第 `97-119` 行按 ±MaximumShiftSeconds 选最佳 RMSE；`WaveComparisonConfig.FromContract` 当前把默认最大移动设置为 `0.050` 秒。**严格响应时间要求禁止在主判定用此平移抵消延迟**。
- `Evaluate` 第 `135` 行调用 `Interpolate`，第 `142` 行用 `AbsTolerance + |reference| × RelativeTolerance`，第 `145-146` 行最后点超差但无下一点时未累加超差持续时间。时间覆盖和 out-of-tolerance 应针对可观察区间定义而非把最后离散点自动算零秒。
- `Interpolate` 第 `152-165` 行对所有 signal 作线性插值；对 `bool/enum/counter/step` 有视觉和逻辑假中间值问题。
- `WaveTimeQuality.Describe` 能拒绝 source gap/overflow/mixed/invalid，但在`Replay/Simulation` 内可返回`CanJudge=true`；该“可供算法比较”绝不等于“已验证可生产来源”，还需要独立源身份与 RECORD 门禁。
- `RealTestRecordSession` 的 `WaveComparisonPendingArchive` 仍与 `RunFact.Validate` 非空种类仅接受 `DirectDbcSignal` 相冲突；因此“算法正确”不足以表明系统正式判定链可用。

**保留成熟资产，不推倒重建**：不可变 EvidenceStore/SourceRevision pin、基本 TimeQuality、Gap/Overflow 检查、已有图表只显示抽稀，都是应继续复用的基础。

## 3. 算法分型与严格的业务规格

| Rule | 推荐定义 | 适用 | 禁用默认 |
| --- | --- | --- | --- |
| `ObservedStateTransition` | 从本次触发前已知初始状态进入目标枚举；规定观测最迟时刻、是否要求持续有效报告；时序误差有明确语义 | Apply/Release、诊断会话状态 | 线性插值后的中间状态、自动 shift |
| `StateHold / ForbiddenStates` | 在实际可观测窗口内，持续保持目标；检查每次有效新帧、Freshness、非法状态、空白区段 | EPB 已施加/已释放/故障位 | 最后一帧无限期保持 |
| `ObservedSampleBounds` | 对所有本次真实新帧上报的报告数值做上下界检查，并明确声明只证明**这些采样值** | ReportedCurrent/Voltage/Temp | 假设任意帧间真实物理值都合格 |
| `Settling / SteadyWindow` | 在规定时窗内根据有效采样值检查均值/极差/方差、最大样本间隔/有效覆盖；数值质量先过 Gate | 慢变化平台值 | 任意 3点够用 / 平滑掩盖禁止瞬态 |
| `ReferenceEnvelope` | 信号语义、触发/负载/DBC/Reference pin 先一致；给明确分段上下界、误差、尾部未覆盖区 | 有正式已发布参考的报告数值动态特征 | 单独全局 RMSE 或点数 passRatio 决定 PASS |
| `CrossSignalSequence`（需额外审查） | 同一录波组中各异步轨道有确切触发/时钟来源/丢帧区间；表达事件 A 先于 B 等可观察要求 | ECU 多状态联动 | 把共用 X 轴视为硬件同步 |

对于离散状态首版建议用**有限状态机/事件规则**，不需要先研发 DTW/神经网络/FFT。数值可先采用绝对/相对容差 + 时间有效覆盖和统计特征，严格按已批准 Profile 定公式，不能用统一默认 5%。

## 4. 核心判定的时窗逻辑（设计 pseudocode，非当前 C#）

### 4.1 质量关（在任一算法前执行）

`
freeze SignalPin/DbcSha/RuleVersion/ReferenceId+Sha(if required)
capture session with one physical USB epoch
ensure source subscription ready BEFORE stimulus
record explicit ActionCommand/SignalTrigger provenance
collect raw CAN, decoded samples, gap + loss facts
close capture with EndReason
if malformed config or mismatched versions/source: ERROR
if required window unobservable, clock bound unknown, loss/overflow/invalid or no fresh target: INCONCLUSIVE
otherwise evaluate chosen requirement from FULL ORIGINAL SAMPLES
persist Capture+Comparison+RawCAN+Run/StepOccurrence and verify before formal PASS
`

区别：若观察健康且已获得充分的全窗口报告，明确检测到错误状态/超限→FAIL；若只是本次目标 CAN 停发/没有足够样本，就不能一律当 DUT FAIL。可将“必须发送指定周期报文”作为**独立通讯功能规则**，其可观测违约已证时可判 FAIL。

### 4.2 状态变化和保持

- 触发前必须证明可接受的初始态，除非规则显式允许 `AlreadySatisfied`。不能从采集开始后任意一帧目标态推断确实执行了 Apply。
- 状态报告只在 ECU 给出新帧时确定。画面可以在 `MaxAge` 范围使用有限 ZOH 直观展示，超出有效时间画“未知”；报告不应称两帧间真实物理状态未变化，除非 ECU 协议有锁存/事件保证。
- 对“在 T 内**接收到了状态**”采用 `HostObservedByDeadline`，既有真实主机刺激事件又有目标新帧可执行。对于“ECU 物理状态在 T 内改变”，目前只确认 HostArrivalOnly，无经过独立实验确认的时延上界时不得准出。
- 响应时间区间 [L,U] 可信、与边界 T 比较时：`U≤T` PASS、`L>T` FAIL、交叉边界 INCONCLUSIVE（其它质量已过；如无界不可套）。
- 状态顺序: Invalid/Forbidden 与 Goal 不同；未经允许的中间状态即使最后达到目标也可 fail（按规格）。

### 4.3 数值波形和稳定性

- `ObservedSampleBounds` 逐原始有效新帧判断，保存最严重超限帧与 raw bytes；只证明采样点，不推出未采到的尖峰。
- 对 `SteadyWindow`：需要固定真实时窗+最小有效样本/时间覆盖、最大 gap/age；输出 `mean, min, max, peak-to-peak, robust median/quantile` 作诊断；用于正式 PASS 的指标须经批准。
- 可用 `MAD (median absolute deviation)` 对离群影响做**诊断统计**，但不得把同一规则下的关键真实极值裁掉后再据此放行；“去异常值”必须满足确切协议无效性规则，不是因为该点不符合参考。
- 允许带宽/滞回作为**明确的状态识别规格**防止阈值边缘抖动（例如经测量批准的 High/Low thresholds/hold duration）；不能把已真实观测的禁止状态或越限样本删掉。对于必须检测瞬态的约束，任何 smoothing/debounce 都只能用于非判定的辅助视图。
- `ReferenceEnvelope` 需要声明具体相对/绝对容差组合。MathWorks 对特定 signal 的绝对/相对/时间容差、插值方式提供灵活配置，不代表 AutoTest 应拿统一值复制： https://www.mathworks.com/help/simulink/ug/how-the-simulation-data-inspector-tool-compares-time-series-data.html 。
- 时间对齐分 `TriggerAligned`（有来源的同一动作锚点）与 `ShapeOnlyShift`（工程诊断、记录偏移）；严格响应时序下不得自由按最小 RMSE 搜索后 PASS。禁用不受控 DTW：它可能把迟滞/反常响应形状挤压为“接近参考”。

## 5. 稳定性研究的实验设计：同一 DUT/多 DUT/时窗/负例分开

**实验不是指定必须测试固定 N 次后就宣称合格**；N、风险级别、检测率与允许误拒率由 ECU 测试规格和现场工况共同定。在研究报告中应列清实际次数、数据量和统计置信范围。

| Study | 控制变量/动作 | 观察指标 | 是否可以判定算法合格 |
| --- | --- | --- | --- |
| `A-DETERMINISM` | 同一不可变 capture + ref + profile + build 重复计算 | 结果 bitwise或约定浮点误差范围的一致性、异常段一致性 | 可做自动测试，不能证明现场 |
| `B-SHORT-TERM` | 同一台已知合格 ECU，同工位同动作多轮 | 值误差/观测响应时间分布、Pass/Fail/Inconclusive次数，CAN gap/队列丢失 | 证明特定条件下重复性，不推广所有 ECU |
| `C-INTER-DUT` | 多个已确认合格 ECU，同已发布规格/工况 | 合格件自然差异、参考过拟合、误拒；需要独立样本 | 仅按覆盖的型号/版本与工况说明 |
| `D-NEGATIVE` | 已知故障 DUT，或安全可恢复、协议和规范可判的异常输入 | 真正缺陷识别、边界响应延迟、非法状态、漏报 | 对覆盖的缺陷类型评价，不能声称检测所有缺陷 |
| `E-HOST-LOAD` | WPF 多轨/缩放/大量 CAN/磁盘空间压力 | 采样间隔/接收丢包/recording loss 的影响与 Inconclusive分类 | 不得用 GUI 平稳作为无丢帧证明 |
| `F-RESTART-EPOCH` | DUT 换台、USB断开、主控重连、项目/DBC切换 | 是否拼接旧帧/旧参数/旧 DUT | 必须严格不串录 |
| `G-REFERENCE` | 与若干**未参与拟合**的合格/异常录波盲测 | 假 FAIL/假 PASS/不可判定与风险区间 | 需要真实标签真值，不能从算法自己判断真值 |

**阈值标定**：以产品规范为主、重复性和测量不确定性为约束，使用留出样本作独立确认。可为诊断输出分位数/箱线与波动，但不以一台 ECU 一条良品曲线形成“±5% 全型号生产标准”。对多阈值连用时必须清楚联合放行逻辑和误判概率，不能因每个子指标“看着都稳定”就默认总系统漏判率可忽略。

## 6. 评价算法稳定性的四个正式统计输出

不把因故障造成的不可评价硬算成 PASS 或 FAIL；四态的比例都应单独报告。
- `QualifiedGoodPassRate = PASS among externally verified good DUT trials with fully evaluable data`；合格 DUT 的 FAIL 是假拒收，INCONCLUSIVE 是测量可用性问题，须另计。
- `KnownBadRejectRate = explicit FAIL among externally verified bad DUT trials`；已知坏件的 PASS 是严重的假接收；INCONCLUSIVE 是未能得结论，不能伪称“已识别坏件”。
- `InconclusiveRate = quality/time/source INCONCLUSIVE / all attempted cases`；反映工位/采集能力的稳定性。
- `RecomputeAgreement = same immutable input + pinned algorithm repeated evaluations agreement`，不应因 UI 下采样、CPU 负载、外部文件变化而改变。

给出每类分母、测试轮数与信赖区间；“观察到0次漏判”不等于真实漏判概率为0。对一个产品判定声明必须说明检测范围，而不是只给“算法准确率98.7%”。

## 7. 特意针对旧算法写的红灯测试向量（必须在修复前复现，修复后守门）

| ID | 刺激/波形/边界 | 正确预期 |
| --- | --- | --- |
| WS-01 | enum0→2，中点线性得1，1是错误/另种有效状态 | 不允许中点虚构状态1；状态事件法 |
| WS-02 | ECU 达标晚40ms，Reference比对允许50msShift，但响应规格只允许20ms | 有明确时间事实则FAIL，不准“移齐PASS” |
| WS-03 | 参考0-1秒1000样本、后9秒仅1样本，实际后9秒缺失 | 时间窗不足→INCONCLUSIVE，点比率无效 |
| WS-04 | 最后一个实际数值越限，后续无任何新帧 | 不把越限持续0s认PASS；时窗尾部UNKNOWN |
| WS-05 | 需要维持2s，只有前0.5s有效报文 | INCONCLUSIVE |
| WS-06 | 10Hz 报文要求检测5ms瞬态且无ECU锁存保证 | UNSUPPORTED_OBSERVABILITY |
| WS-07 | UTC调整回退而monotonic正常，或monotonic跳变 | 使用明确定义时钟域；异常拒绝/标记 |
| WS-08 | 始终未真正完成订阅，触发时仅显示Ready文本 | 不通过质量门禁 |
| WS-09 | 多轨一条没有新帧但另一条有 | 联合判定INCONCLUSIVE，不用其它帧冒充 |
| WS-10 | Reference Replay、WaveTimeQuality.CanJudge=true | 仅离线可比较，正式生产引用拒绝 |
| WS-11 | 两个不同Loop复用同 captureKey | 每轮真实 CaptureSession/Occurrence不同，不共享旧 actual |
| WS-12 | 样本来自旧 DUT/旧主控epoch却DCF/信号名一致 | 归属不匹配则ERROR/INCONCLUSIVE，正式不PASS |
| WS-13 | 图上抽稀至2000点，隐藏一个极值 | 判定必须从完整 raw sample 执行，不能从绘图点 |
| WS-14 | CRC/AliveCounter规则可用时跳号；无counter时目标帧间隔异常 | 可发现的丢失触发质量不足；不得谎称可检测所有无counter丢帧 |
| WS-15 | 正式WaveCompare PASS的StepFact写入验证器仅识DirectDbcSignal | RECORD路径红灯→修正式有限Wave evidence后变绿，不禁用校验 |
| WS-16 | 同一已批准规则多次执行，合格件因USB负载时而Inconclusive | 与真实 ECU FAIL分开归因，量化测量可用性 |
| WS-17 | 阈值附近样本稍有抖动，算法滞回打开/关闭 | 若业务没有批准滞回，结果不得因默认滤波自我放宽 |
| WS-18 | 已知坏件缺陷发生在被平滑/剔除的采样点 | 不能为了稳定性忽略真实不合格点 |

**时间实测补充**：只凭当前 USB 0x41 Windows HostArrival 时钟不能对 DUT 内部物理限时作确定保证。若需要物理事件相对 ms 精度，须借独立测时工具/硬件协议增强建立延迟上界；该任务需 FW/DEVICE 管理确认，不应在本 WAVE 研究里凭空更改 USB 协议。

## 8. 最小研发闭环与 Gate

1. **研发样本**：先从真实 HydEPB 工程 DBC 中选定有协议定义的枚举状态和一项数值信号；审核实际 ECU 软件、CAN 发送周期、生产相关目标。无实测数据时阈值保持`UNQUALIFIED`。
2. **算法纯函数**：保留旧`WaveComparator`接口作为兼容/迁移 wrapper，不让旧默认50msShift悄然进入正式时限规则；引入强类型`RuleType + InputSignalSemantics + QualityPolicy + AlgorithmVersion`（具体结构需跨 TEST/RECORD审查）。
3. **采集规则**：实际采样质量及时间源先通过、时窗完整、独立源/Reference冻结后才 evaluate。
4. **自动测试**：WS-01～18 + W-05/W-10 既有负向向量，实际 RECORD run写库红/绿整合，Windows Build/Test报告完整。
5. **现场资格**：多轮合法重复、故障已知真值、断连/时钟/丢帧、跨ECU扫描归属、完整报告重开，真机与算法研究分开记证据。
6. **发布限制**：在没有测时上界/足够正负样本前，R1 可申请`主机观察意义的波形判断`，不能声称 ECU 物理高速信号或所有故障零漏判。阈值、假接收/误拒风险和适用范围仍需用户与具体产品规范审查。

**本次只落档研究/算法准入，不改HOST/R1/main/固件，BUILD/FIELD仍未运行。**

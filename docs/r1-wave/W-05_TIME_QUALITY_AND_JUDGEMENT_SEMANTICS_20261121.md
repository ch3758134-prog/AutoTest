# W-05｜ECU CAN 波形判定数学语义、时间不确定度与防误判规则 v0.2

**2026-11-21 / Issue #71**。对照 `R1@e5a9e13ad522ddaf9092f44099f2ef26bdf1e19b` 的 `WaveComparator`、`WaveComparisonConfig`、`WaveTimeQuality`、`WaveSampleBuffer`、`WaveCaptureService`、`LiveDbcSignalService`、`HardwareCanReceiveBridge`。**状态：算法研究候审；未编写替代比较器；真实 ECU 测时 NOT_RUN。**

## 1. 先说明能证明什么，不能证明什么

CAN/DBC 信号波形是`离散报文观察值序列`：

- 观测到的 `x(t_i)` 是在某次 CAN 帧中编码、由 DBC 转换的 ECU 对外报告值；不是随时刻连续可读的传感器真值。
- `t_i` 在当前源码中来自 USB `0x41` 被 Windows 主机接收的 `UtcNow + Stopwatch.GetTimestamp`，**不是 ECU 计算、采样或发送帧的硬件 timestamp**。
- 即便报文 10 ms 周期发送，也**不能**据此证明 ECU 在两帧间没有持续 2 ms 的故障，除非 ECU 协议保证此类瞬变会被锁存并在下一帧报告。
- 仅比较参考采样点与实际样本，不允许推断被遗漏的高频尖峰；“电机电流报文值”不得改名为“独立测得电机相电流”。
- 两个时间轴都使用“开始采集”当 0，会掩盖动作发生的实际偏移；对于动作响应时序必须以**相同含义且有证据的动作/触发事件**为轴。
- 波特率选择 `500 kbit/s` 绝不等于 `500 ksps` 样本率，具体采样周期需要真实 ECU CAN 报文验证。

官方相符的证据：TraceTronic 对 bus 信号的不连续采样/空白时间点和同步概念的说明 https://docs.tracetronic.com/help/ecu.test/2026.3/en/trace.check/Manual/Significance-of-time-stamps.html 。

## 2. 四种信号语义（不要让 double 类型自动决定）

| 语义类型 | 数据特征 | 允许的时间估计 | 常见判定 | 禁止 |
| --- | --- | --- | --- | --- |
| `DiscreteState` | DBC enum、状态机、开关标志 | **零阶保持显示**，有最大新鲜度截止；状态改变只知道落在两个有效报告之间的某个区间 | 进入目标状态、顺序、超时、保持（仅可观察范围） | 线性插值产生 0.5/1.5 等不存在状态 |
| `Counter` | 帧计数、累计计数、脉冲总数 | 逐样本增量，有明示溢出宽度/重置规则 | 无非法回退、窗口内增量范围 | 当作连续模拟量进行插值 |
| `ReportedNumeric` | ECU CAN 上报电流估值、电压/温度等 | 默认原始采样点；**仅当协议语义允许、相邻间隔在可信范围内**才可按用户声明的线性插值辅助分析 | 采样值越限、稳态均值、平台、持续性观察 | 凭插值断言没漏掉真实尖峰或连续时间都合格 |
| `EventFlag` | 故障锁存/事件状态位，可能报文不周期 | 必须知道事件的保持/清除语义和最大新鲜度 | 事件曾发生、事件出现次序 | 无帧就认定事件不存在 |

**不能只根据 DBC 物理单位**决定是哪一种；`SignalSemantic` 应是受版本管理的 TEST/WAVE Profile 字段。对 enum 的非法值、DBC multiplexing 未满足、CAN 数据长度不一致/未更新/无效标志，按坏样本/不可判定处理而不是数值 0。

## 3. 必须先完成时间/采样的质量门禁，再讨论容差

一次判定的 `QualityEnvelope` 至少包含以下输入，全部可回溯：

- `CaptureSessionId / StationEpoch / CanGeneration / ConnectedDeviceIdentity` 和同一次工作流 `RunId/CaseAttemptId/StepOccurrenceId`。
- `ClockOrigin`：HostArrivalOnly / DeviceSample ...；严禁将不同的来源合并；主机 UTC 用于显示与报告，**可靠的 elapsed-time 分析需要同一 session 已知频率的 monotonic timeline**，并记录同步/跳变质量；现有比较器使用 UTC 相对秒，改造前须确保系统时钟扰动不会误判。
- `SourceFrameMetrics`：目标 CAN ID 总数、实际 DBC 有效信号数、首末新帧时间、实际报文周期 min/median/p95/p99/max、失效/重复/未关联事件数、总线帧队列/订阅队列丢失差分。
- `CaptureMetrics`：真实启动/订阅就绪、预采样、动作锚点、结束边界、窗口两端真实覆盖、连续有效片段、原始/有效/丢弃点数，边界新鲜度。
- `Completeness`：是否在执行窗口拥有**目标信号真正的新帧**，而非历史 `Latest`；是否有数据流被取消、USB epoch 变化、WaveBuffer Overflow、有效状态突然中断。
- `SignalPin`：完整 DBC id/rev/SHA、帧 ID+扩展帧、Role、SignalName、Unit、可用/无效枚举编码、必要的同帧 quality bits。
- `Judgeability`：P0 必需窗口可被证明覆盖、局部间隙小于 profile 经过现场验证的允许值、起止边界没有未覆盖异常。

**源代码现状**：
- `WaveTimeQuality.Describe` 校验样本时间严格单调、数值有限、信号/单位一致、Gap/Overflow/InvalidInput；保留这些严格规则。
- 但它的 `CanJudge=true` 对 `Replay/Simulation` 也成立；需要在**生产参考审批与生产真实 verdict 层**另查 `CaptureProvenance.LiveHardware + VerifiedSource + Run/Device/DBCPin`，不能让算法层的“可比”自动升级为“真实 ECU 可信”。
- `HardwareCanReceiveBridge.DroppedBeforeDecode`、Hub/信号订阅 drop、信号无更新属于不同类型；必须按 capture begin/end 的 epoch **做差分且归属本次窗口**。一个全局非零历史丢帧计数不能永久污染未来每条波形，也不能被忽略。
- 若 capture 在停止前触发了 `WaveCaptureService` 的定时退出、但没有收齐窗口边界数据，`有两个点`仍不足以证明正常。
- 失去 USB/DBC 数据后若始终没有新帧，不能等待下一帧才知道已丢失；独立 `maxSilence / maxAge` 健康计时门禁是必要的。

## 4. 触发锚点、相对时窗与响应时间：最关键的防假 PASS 设计

### 4.1 时间轴要有明确定义

推荐：

`PRE_ARMED → CAPTURE_READY → STIMULUS_SENT → (可选) STIMULUS_OBSERVED → EXPECTED_RESPONSE_OBSERVED → HOLD_WINDOW_END → STOP → QUALITY → JUDGEMENT`。

- `CaptureReady` 是真正订阅完成，并且可以证明没有漏掉开始前的第一帧；不能只是调用一个异步方法。
- `StimulusSent` 是当前 TEST 执行器发出 KEY/IGN/CAN 指令的真实动作节点事件，不是采集窗口 `t=0`。如果缺少更强硬件证据，报告必须写明它只是**主机指令发出时刻**。
- `StimulusObserved` 需要额外真实传感器/端口证据才可标记物理动作已完成；如果没有，**不能凭 USB Write/ACK 冒充**。
- `ResponseObserved` 是目标 CAN 帧反映了状态；如信号的“真正 ECU 内部转换时刻”没有被标时，报告只能给出**报文观察意义**的响应延迟。

### 4.2 两次样本之间的事件时间不是确定值

对离散状态：
- 若最后一次确定“旧状态”的有效报告时刻为 `t_old`，第一次确定“新状态”为 `t_new`，且期间没有丢帧并已知报告协议语义，状态报告的变化只可定位到**观测区间**，不能直接断言物理 ECU 在 `t_new` 精确变更。
- 进一步由主机时间推断真正物理响应，还需要**已上界的报文发送延迟、USB 等待/排队和动作锚点误差**；否则上下界是不确定的。
- 若各链路总误差经现场标定可给出可信上界 `Uearly`、`Ulate`，对经证实的 host-observed 时差 `d`，可以保守表示 `[d−Uearly, d+Ulate]`，以此而不是“自动移位后的最好 RMSE”进行阈值判定。**这种 U 未获得前，不允许自己取一个常数**。

### 4.3 判定“限时不超过 T”的规则

若可证明事件响应时间落在 `[L,U]`：
- `U ≤ T` 且其它质量全部满足 → `PASS`。
- `L > T` 且可以排除缺失旧样本/错误锚点等不确定因素 → `FAIL`。
- `L ≤ T < U` 或上下界根本无法通过硬件证据确定 → `INCONCLUSIVE`，**不应直接 PASS 或强制 FAIL**。
- 如果目标状态缺失，而有完整且足够精度的观察窗口，且明确要求最迟 T 内有目标报文，则可以按已定义观察规格 `FAIL`；如果观测窗口存在 gap/超时/无法证明已看完必要窗口，则 `INCONCLUSIVE`。
- 若用户只想判“**在 T 以内至少观察到一帧目标状态报告**”，可以定义 `HostObservedByDeadline` 并明确是接收侧目标，而不是未经证明的物理时限。这样的测试可能在硬件时钟尚不合格时仍有条件可用。

### 4.4 不应统一自动对齐

`WaveComparisonConfig.FromContract` 当前默认 `MaximumShiftSeconds=0.050`，`SelectAlignment` 搜索最小 RMSE，可能校正掉 50 ms 内的 ECU 延迟。R1 正式响应时间判定默认 `MaximumShift=0`；任何非零对齐应在 profile 明确选择`仅形态比较`并记录实际 shift，**不得同时将被平移过的曲线证明成“响应时间符合规范”**。

可允许`SignalCrossCorrelation`/RMSE 最优平移**仅为工程诊断**（或明确声明“无响应时间要求”的非生产形态比较），不可悄悄作为生产时间验收门槛。数学上可对齐不等于物理上同步。

## 5. 按观测语义选择判定方法

### 5.1 StateTransition：状态进入 + 稳态保持

Profile 至少包含 `InitialStateCondition, TargetState, TriggerRef, ResponseDeadline, HoldStart, HoldDuration, MaxSampleAge, MaxGap, ExplicitDomain(HostObserved/PhysicalBounded)`。

规则：
1. 在 trigger 前拿到足够新鲜的初始状态基线；否则已处于目标状态不等于实际响应成功（可在明确声明`AlreadySatisfiedAccepted`时例外）。
2. trigger 后必须收到新鲜**本次窗口内**的状态样本；Old→Target 顺序是必需时不许缺失。
3. 响应时限按 4.3 的可证边界/观测语义判定；移位优化一律禁用。
4. Hold 判定必须有持续更新的有效状态证据，`MaxAge` 内使用有限 ZOH 仅作观察建模。若最后一个状态样本在 hold 结束前过期，不能凭“它是最后一条”宣称保持成功。
5. 若规格需要“绝无 5 ms 瞬断”，而报文可能 100 ms 一帧且没有 ECU 锁存/快照保证，当前硬件**无法证明**；校验器应标记 `UNSUPPORTED_OBSERVABILITY`，不能当 PASS。
6. 所有值变换符合 DBC signal revision，状态中的 2/3/255 等无效码不默认为其它合法状态。

### 5.2 NumericEnvelope：数值限值与持续性

Profile 选择 `min/max`、允许绝对或相对容差、`WindowStart/WindowEnd`、`RequiredCoveredDuration`、`MaxGapSeconds`、`MaxAge`、必要时离散采样上下限**与**运行时上下限分开显示。

- **SampleLimit**：对真实有效样本点逐一判上下限，可以证明“所观察的帧在范围内”，不能推断样本间无尖峰。
- **WindowCoverage**：需要证明时窗被连续的可新鲜度约束的观测支持；持续越限时长只能按真实有效区间计算，不可仅用“超限点数”代替秒数。
- **SteadyState**：指定 settle 延迟、窗口长度、可观察 RMS/均值/峰峰值，必须保留基线/极值/有效时长；允许区间不应来自单条 Reference 的偶然噪声。
- **PeakCurrent**：若只是 ECU 的 CAN 报告估值，报表必须显示 “ECU reported current”；没有外部高采样率探头不能声称“没有电机瞬时过流”。

### 5.3 ReferenceSimilarity：参考曲线形态

在 `Reference批准/SHA/型号及上下文一致` 后，按选定信号语义采用不同策略：
- `DiscreteState`：按事件/状态窗口进行比较；Zero-Order Hold 且 `maxAge` 有界；关键状态进入时间/次序需单独判定，**不可将离散值线性插值**。
- `ReportedNumeric`：如果明示线性插值且间隔足够小，可按两段相邻真实样本插值做参考诊断；结果名称写 “interpolated reported values”，不可宣称未经采样的峰值保证。
- `Coverage`：同时检查`时间覆盖率`、起止边界、最大连续缺失、采样点来源；点覆盖率只能作为诊断指标，不应独占 verdict。
- `Error`：选择 `AbsoluteLimit` 和 `RelativeLimit` 的组合规则必须明确（例如 `max(abs, relative×|ref|)` 与旧实现 `abs + relative×|ref|` **不同**；发布时固定公式和算法版本）。在参考值接近 0 的区域必须有绝对容差，不能出现浮点特殊值。
- `TimeShift`：正式**时序合格**判定为 0，形态比较可在明确选定政策下设有界 shift，但报告 shift 和不允许用它抵消延迟的事实。
- `OutsideDuration`：按照在采样质量满足的有效时间段内的超容差区间总长度计算，并**明确窗口结束处的未确认尾段**；末尾失败点不能自动贡献 0 秒、稀疏时点不能延拓到无数据的未来。
- `RMSE`：辅助诊断，不应该替代硬性响应边沿、窗口/强制安全边界。

MathWorks 文档明确区分 ZOH/线性/无插值，并允许每个信号有不同容差；其绝对/相对组合示例是 `max`。**这是参考方法而非 AutoTest 必须沿用的唯一数学定义**：
https://www.mathworks.com/help/signal/ug/how-the-simulation-data-inspector-tool-compares-time-series-data.html 。

## 6. 结果必须有独立的判定质量状态

| 情形 | 业务意义 | 必须结果 | 可否生产 PASS |
| --- | --- | --- | --- |
| 所有规则均经可用数据满足、归档完整 | 满足已发布规范 | `PASS` | 是，仍需 TEST/RECORD 正式门禁 |
| 有足够可信的观察明确证明违反约束 | DUT 功能不满足 | `FAIL` | 否 |
| 无数据、缺时窗、时间上界不足、丢帧、采样过疏、设备重连 | 无法可靠判断 DUT | `INCONCLUSIVE` | 否 |
| 参考已撤销/损坏、配置非法、DBC/算法版本冲突、步骤绑定错误 | 测试配置/系统错误 | `ERROR` | 否 |
| 工作站人工停止、其他步骤失败而本项未执行 | 本项没有形成判定 | `ABORTED/SKIPPED`（执行状态） | 否 |

区别：`FAIL` 表示已得到足够证据证明产品没有满足规则；`INCONCLUSIVE` 表示没有足够证据决定。`ERROR` 表示系统自身无法执行合法分析。**状态必须进入报告与批次汇总，不把三者都变成“波形失败”。**

现有 `WaveComparator.Compare` 有质量拒绝初步能力，但主结果持久化、部署级 verdict 还需 W-06 合同约束。

## 7. 当前 R1 的具体偏差及应修方向

1. `WaveComparator.Interpolate` 对任何信号都线性插值：拆为离散状态/连续报告量/无插值策略，按 Profile 选择。
2. `PassRatio = Passed / Compared` 且 `Coverage = Compared/ReferencePoints`：保留为诊断，但正式要有**时窗完整性、有效秒数、最大缺口与窗口边界**。
3. `OutsideDuration += rt[i+1]-rt[i]` 对最后异常点无时长：区分已观察异常、未证尾段和真正的“连续超差时间”。
4. `WaveTimeQuality.Describe` 仅验证输入一致性，允许`Replay/Simulation`：production admitted source gate 必须独立校验。
5. `WaveCaptureService` `StopAsync` 只要求至少一个有效点就返回证据：下一层质量评价必须明确反映完整窗口，不能“捕获成功”==“合格”。
6. `WaveComparator.RelativeSeconds` 使用 UTC delta：时间精密比较应改为经频率核验的 monotonic session timeline；UTC 作为 wall clock，用于报告/审计，保留变更检查。
7. `ConfiguredWavePort` 按 `captureKey` 管理而且 `_completed` 阻断同名复用：循环/公共流程须以每次真实 `StepOccurrence/Iteration/CaptureSessionId` 区分，而非全局字符串唯一。
8. `WaveReferenceCatalog.Save` 在每次保存时生成新 AssetId、Revision=1，生产 Reference 实际修订关系/审核未形成；参见 W-06。
9. `LiveDbcSignalService` 已有 Latest 的 maxAge 保护，但 Wave 的 stream 接受新样本需要明确报告 channel overflow、无新样本和重连是否造成无解的 quality。
10. `WaveStep` 的触发没有单独动作生命周期对象；仅靠停止步骤的时间不能回推真正开始动作的时刻。

以上是**静态源码结论，不意味着物理采样性能值已获验证**。

## 8. 对抗性测试向量（纯算法必须全部覆盖，不能代替现场验收）

| ID | 最小测试场景 | 正确期望 |
| --- | --- | --- |
| W2-01 | 枚举状态 0→2，取中间时刻 | 绝不能插值出合法状态 1 |
| W2-02 | 参考动作在 100ms，实际在 145ms，允许 shift 50ms，但要求响应最迟 120ms | 不允许用 shift 掩盖延迟；FAIL 或如时间上界无证则 INCONCLUSIVE |
| W2-03 | 参考 1000 个点密集在前 1s，后 9s 只 1 点且实际缺失 | 按点 99% 覆盖不得使 10s 窗口 PASS |
| W2-04 | 目标保持 2s，实际最后有效样本在 0.5s | INCONCLUSIVE，不自动延拓 1.5s |
| W2-05 | CAN 100ms 周期，要求发现 5ms 瞬断，协议无锁存保证 | UNSUPPORTED_OBSERVABILITY，不能 PASS |
| W2-06 | 预计 10ms 报文实际最大 200ms 无更新 | 缺口/新鲜度拒绝 |
| W2-07 | 连续两条同 Timestamp 且值不同 | 不混合也不任意排序，明确时钟/记录冲突 |
| W2-08 | 实际信号相同名称但 DBC 修订/SHA 不同 | ERROR: SIGNAL_REVISION_MISMATCH |
| W2-09 | enum 新出现的 DBC invalid value（如 255） | INVALID_SAMPLE / INCONCLUSIVE |
| W2-10 | 参考来自 Replay，采样质量逻辑合法 | 工程可分析、**生产参考拒绝** |
| W2-11 | 真实数据来源从 HostArrival 切成 Replay | MIXED_CLOCK / INCONCLUSIVE |
| W2-12 | Wave 数据前后都是真实信号，但含某次 USB 断开重连 | 不拼接旧 epoch，INCONCLUSIVE |
| W2-13 | Capture 首次收到目标值时动作已经发生 | 无有效初始状态，不能把“已达目标”冒充完成转移 |
| W2-14 | 没有收到任何目标 CAN，窗口完整到期 | 观测目标如果定义为必须收到帧则 FAIL；若链路健康无法证明则 INCONCLUSIVE |
| W2-15 | 参考数值接近 0，relativeTolerance 非零 | 严格按已发布的绝对/相对容差公式，不 NaN/除0 |
| W2-16 | 只有最后一个参考点越限 | 不应将越限持续时间自动当 0s；尾部必须有证据或标为无法确定 |
| W2-17 | 两次样本间存在 10ms 尖峰但上报周期 100ms | 不能宣称电流峰值无异常；可观察规则才允许 PASS |
| W2-18 | 基准和实际开始/结束时间不同，仅中心重叠 | 起止边界没有质量覆盖，按配置拒绝或有条件局部诊断 |
| W2-19 | WPF 画图仅下采样至 800 点 | 结果必须由完整原始样本计算，不能因视觉下采样改 verdict |
| W2-20 | Captured sample SourceEvidence 不属于这次实际 Run CAN_RX | RECORD 必须拒绝正式 PASS |
| W2-21 | Loop 两次重复调用同一 captureKey | 每个 StepOccurrence 独立证据与结果，不互相覆盖、不拒绝合法第二轮 |
| W2-22 | 参考只用 name 解析，工程中后改已发布引用 | 必须固定 ReferenceId/Revision/SHA，不能静默改参考 |
| W2-23 | Stop/Cancellation/设备断开竞态，已收两点但窗口不完整 | 证据可保留；结论不可 PASS，记录终止原因 |
| W2-24 | UTC 时钟跳跃但同单调 session 仍正常 | UTC 用于审计，判定 elapsed 不能随系统时钟跳变 |
| W2-25 | 达标时刻可证区间 [95,110]ms，规格 100ms | INCONCLUSIVE，不能按 95 或 110 拍脑袋确定 |
| W2-26 | 达标区间 [80,95]ms，规格 100ms，所有数据质量证明齐全 | PASS（只对该观察指标与规格） |
| W2-27 | 达标区间 [110,120]ms，规格 100ms，所有数据质量证明齐全 | FAIL |
| W2-28 | 保持窗口在末端缺少一帧，系统仍保存了比较 evidence JSON | artifact 可审，**但 DUT 不能 PASS** |

注意 W2-14/25～27 的结果受具体 `HostObserved` 还是 `PhysicalBounded` 合同约束；没有可信误差界时不能自行创造一个区间并代入测试。所有向量需明确构造时间序列、样本/缺口/原始证据和期望错误码，由 R1 通过 CI/Windows 跨层复现。

## 9. 真机标定的决定性数据：不提供数字就不应批准“时间精度”

至少记录以下数据，按目标 CAN ID/真实波特率/典型工位负载：
- ECU 报文周期分布 min/median/p95/p99/max；复测多个真实循环以观察边界；
- USB `0x41` 接收 queue drop、CAN Hub drop、DBC subscribe overflow、目标帧健康率、设备重新枚举的 session epoch；
- USER KEY/IGN/CAN 指令从发出到 USB 响应和目标 CAN 状态的**主机观察延时分布**，说明不能代替物理响应时延；
- 不同负载：静默基线/同时 CAN 周期发送/Flash 同时占用（若该场景允许）/WPF 图表同时刷新；
- 参考采集和实际采集的 DBC/ECU 固件/工位连接/操作状态/环境信息；两个方向，明确 DUT 是否合格；
- 现场**故意失效**：拔掉主控 USB、临时失去目标 CAN/错位参考/错误 DBC、制造波形不满足要求，观察是否 INCONCLUSIVE/ERROR/FAIL 而不是 PASS。

**最低可判响应门槛**只能在上述真实数据和需求规格合并后给出。不填“默认 10ms/50ms”的精度承诺。正式现行 FW 没提供高精度 timestamp，不建议以当前主机时序进行精确电气响应时间的出厂保证。

## 10. 研究出口与代码审查边界

- 先让 R1 的 TEST/RECORD 正式波形 evidence 能持久化，不会因 `JudgementEvidenceKind` 合同冲突导致 PASS 路径中断；
- 再用单一真实 DBC 信号、已发布参考或明确状态功能规则，修复**异常/质量门禁/无隐藏自动移位**，在 Windows 实机一次“正常 PASS+受控 FAIL+故障 INCONCLUSIVE”；
- 通过之后才推进带特征点的高级算法、多个通道同步、完整包络曲线和 MF4 导出；
- 源代码变更必须在研究分支或经审查的 R1 集成分支进行，禁止用 Replay 单测的 PASS 替代实物合格声明。

本稿是算法与可观测性设计候审，尚未实施和现场验证。

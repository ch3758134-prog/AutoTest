# W-10｜波形数值准确度、时间可信度与生产判定准确性保障体系（技术与验收合同）

> 2026-11-21；Issue #71；最新复核 R1@8e6aaa556e70ce025a18c20abeb331fa812b055d。仅深入研究/接口准入条件，不代表实 ECU 精度已标定、Windows 测试通过或已做源码修改。与 W-05 数学判定、W-06 RECORD 证据、W-08 容差标定、W-09 工具方案互补。

## 一、定义“准确”：不能用一个百分比代替四层证明

### A. 数值正确 (Value Correctness)
软件对**已收到**的真实 CAN 帧，能够按确切 DBC 资源字节和字段编码稳定解码为正确的 ECU **上报值**。应逐层验证 `CAN role/channel → arbitration ID → standard/extended → DLC/frame length → signedness/endianness/multiplexing → raw signal bits → factor/offset → unit → invalid/quality flags`。显示取舍和数值事实分离，不得让格式化的小数位或图形抽样改变判定。

**边界**：即使解码完全正确，CAN 上报的“电机电流”也可能只是 ECU 内部测量或估算值。没有独立校准探头或 ECU 传感器特性证明时，不能推断其与真正电机相电流的绝对偏差。

### B. 采集完整 (Coverage/Integrity)
`Source Frame¶(USB 0x41) → HostReceiveBridge Queue → CanFrameHub subscriber → DBC signal Sink → Wave recorder Buffer → persisted actual + raw log` 的每一级都有可检查的接收数、损失数/恢复事件、容量和质量信息。当前代码对队列溢出有局部标记，却不能仅以全局“曾收到某些 CAN 帧”保证**这一条波形**完整。测试要求按 target CAN ID、当前 Capture session、预期时窗量化数据，不允许使用其它 ID 充数。

### C. 时间可信 (Timing Trust)
**当前真实 R1 的 USB `0x41` 没有 MCU/ECU 硬件时间戳**：时间来自 HostArrivalOnly 的 `UtcNow + Stopwatch.GetTimestamp`。这只能用于主机接收侧观察，且实际运行的报文间隔/USB排队抖动必须被测量。不能自动宣称“波形时间轴精度 = 1ms”，更不能把程序计算的 1ms 输出当作 ECU 物理事件时刻。

### D. 判定可信 (Decision Correctness)
使用明确的`判定目标、触发语义、时间窗口、值域/参考、信号类别、容差、最小质量要求、软件算法版本、正式证据链接`；在已证明可评价的情况下 PASS/FAIL，资料不足时 INCONCLUSIVE，非法配置/来源/指纹为 ERROR。正确的报告和历史可复核性本身也是正式 PASS 的必要条件。

最终产品**不允许**只有“相似度 98% → PASS”这种无法解释的黑箱；也不把“2个有效样本可以插值”升级为“没有遗漏脉冲”。

## 二、准确度指标分类与检测方法（应产生可审查的 Quality Report）

| 指标 | 具体测量与验证 | 可说的结论 | 不允许说的结论 |
| --- | --- | --- | --- |
| `RawCanIdentity` | 校验 USB Port→ECU Role；DLC/ID/帧格式；同时间内 raw bytes/SourceEvidence 可重读 | 捕获的这批 ECU CAN 帧有可核对原始数据 | 所有 ECU 发出的帧都完整到达 Windows |
| `DbcDecodeConformance` | 与固定版本 DBC 的**独立数值向量**比对 endian/signed/scale/offset/mux/invalid | 已知原始帧解码值与预期一致 | ECU 真实传感器的校准误差已证实 |
| `SourceContinuity` | 从 USB source queue、CAN Hub、Signal Subscriber、WaveBuffer 各计 loss/overflow；同一 epoch 按窗口差分，报告目标 ID 帧间隔 `median/p95/p99/max` | 录波期间已观察到的间隙与本系统可发现的丢失 | 无 CAN counter/sequence 仍能识别所有 ECU 端漏报帧 |
| `CaptureWindowIntegrity` | 真正订阅就绪确认、第一帧/末帧、前置基线、触发时间、自然终止/用户提前停止，观测覆盖时间和 maxAge | 窗口可评价的具体区间 | 最后一帧之后一直保持原状态 |
| `ClockCharacterization` | 用主机 monotonic elapsed 序列统计，检查 wall UTC 跳变、不同 USB session epoch、与外部可信时间基准对照 | 主机**接收观测延时**的实测分布和测试边界 | 无外部测时就能证明 ECU 内部动作时间 |
| `ReferenceQualification` | 来源 real/replay、已知合格样机、多次真实 capture、ECU/DBC/工位工况、审查/修订/SHA | 当前 Reference 适用于已批准条件 | 一台 ECU 的一条波形代表所有批次/版本 |
| `ComparatorCorrectness` | 数学对抗向量+已知好/坏真实 ECU 案例、交叉复算、质量故障注入 | 目标规则下的正确/错误/不可评价行为可复现 | 少数样本全部通过就等于零漏判率 |
| `RecordProof` | Run/CaseAttempt/StepOccurrence→actual+ref+algorithm→raw CAN→report artifacts SHA 一致 | 对正式结果可以重开核验 | 只有图像和一条 PASS 字符串也有生产质量证明 |

**波形工具**可用这 8 组指标生成质量诊断，**TEST** 在判定前强制检查它们对应的已发布最低等级。不要把一条源数据不完整的曲线通过曲线平滑或增加 Coverage 设为可判定。

## 三、量化采样精度限制：不用 CAN 波特率冒充采样率

### 例 A：ECU 状态报文 100ms 一帧（仅是说明性例子）

要检测一段仅持续 5ms、且 ECU 协议不锁存的瞬态状态异常，则**即使每条收到的帧都正确，也无法保证检测到**。这不是 WPF 画得不够精细，而是被观测事件不一定被 CAN 报告出来。应返回 `UNSUPPORTED_OBSERVABILITY`（或仅允许声明“已收到的报告值符合”），不能给物理瞬态的生产 PASS。

### 例 B：响应限时 100ms，只有主机指令/主机接收帧

假设工程审查**确实**通过独立方法建立了某项响应时间的保守可信区间 `[95ms, 110ms]`，则这个区间与 100ms 规格边界相交，返回 `INCONCLUSIVE`；`[70ms, 85ms]` 则该项可 PASS；`[115ms, 130ms]` 则该项可 FAIL——**仅当区间是同一明确物理/主机观测指标的、可信来源的区间**，不能从主机时间差凭空加个 ±15ms 自造 ECU 物理时间上下界。

如果 ECU 实际事件发生→组装报文时间的上界未知、USB 路径可能任意积压、动作输出并没有物理生效回读，那么“ECU 内部动作在 100ms 内”的物理时间范围无法推导。可先改判 `在主机刺激发出后 T 内至少收到新的有效目标状态帧` 的**主机观测规格**，或补充硬件时间戳/外部测量经独立任务审查。

### 时间误差预算（概念，不是已经测出的数字）

可建一个受约束的误差预算对象：
`U_stimulus + U_ECUreporting + U_CANarbitration + U_USBhostreceive + U_scheduler + U_clock + U_detection`。

- 如果各上界可保守、独立核验，可组合形成安全的区间上界；如何合成由明确的保守决策规则确定，**不能直接用独立随机量的平方和代替确定的最坏情况上界**；
- 若其中关键量无合理可证上界，则 `TrustedBound = Unknown`，生产“物理响应时限”禁用；
- 由真实工位采集的 p99 接收周期与延时只代表**观测到的统计分位数**，不能代替严格最坏上界。必须在报告标为“测量分布/有限样本”；
- 当当前需求仅是`主机观察到状态/数据的新鲜度`，可以不用虚构物理响应精度，但名称/报告表述要清楚。

符合 NI 关于软件采样时间与硬件定时采样不同的研究：https://knowledge.ni.com/KnowledgeArticleDetails?id=kA00Z000000P9Q9SAK&l=en-US 。

## 四、按特性定义判定准确性的规则

### 4.1 离散状态——用事件/时窗，不用线性曲线匹配

最适合 EPB Apply/Release：
- 必须有触发前确定的初始状态（按已发布规则可例外）；启动 TEST 录波已经真正订阅目标 ID；
- 记录 KEY/IGN/CAN **上位机动作点**而非宣称继电器/按键物理状态同步已证；
- 等待至少一个触发后新鲜目标状态报告，并检查先后次序、deadline、非法状态、更新间隔；
- 维持段要求持续收到新鲜有效状态报告或具备协议明确的状态锁存保证；无新帧的长区间必须画成未知；
- 0/1/2 枚举不得插值 0.5/1.5 并把中间值当合法状态；
- 若数据完整且报告证明发生异常→FAIL；缺关键数据/时间上界→INCONCLUSIVE；规则非法或错 DBC→ERROR。

### 4.2 连续上报数值——区分“观测点合格”与“整个时间窗合格”

- `ObservedSampleLimit`：仅声称真实收到的有效数值样本在界内；适合最初研究电流估值/电压/转速等；
- `TimedEnvelope`：在有周期和 maxAge 证据情况下，按已发布方法估算有效时间覆盖/超限区段，并明确插值方法和不可评价范围；
- `SettlingAndHold`：平台区间内的最大/最小/均值/峰峰值等特征，必须满足有效新帧数量及时间窗覆盖；
- `ActualPhysicalPeak`：如果无独立高速 ADC/探头/可信 ECU 锁存峰值信号，则**不支持**声称捕获真实高频尖峰；
- 对 near-zero reference，绝对和相对容差如何组合必须版控，不能由程序悄悄改公式；
- 报表明确 `ECU reported current [A]`，不是 `motor phase current measured by bench`。

### 4.3 参考波形——先审有效性，再比较；不允许自动移位掩盖响应时间

1. Reference 必须有真实来源证明、审核状态/适用范围、ReferenceId/Revision/SHA 和冻结算法/DBC Pin；
2. **与 ECU 响应时序有关时默认不允许动态时间平移**；可以将对齐优化作为仅供研发形态比较的辅助值，同时独立报告实际响应延时；
3. bool/enum 用阶梯、状态事件比较；连续报告量按 signal profile 选择 `None/ZOH/Linear`（LinearlyInterpolated != physically measured）；
4. Coverage 以时间覆盖、最大 gap、窗口首末真实新帧以及质量事实为基础，不仅按采样点比例；
5. 不可检测的短瞬态不因为 RMSE 很低就合格；末尾最后一个异常点不能自动算 0s 越差；
6. 分别报告值误差、时间差、异常区间、缺测区间和质量；整体 PASS 依赖每个必需规则通过。

相符工业设计：MathWorks SDI 根据信号选择插值与容差、ecu.test trace.check 使用时间窗/触发和 INCONCLUSIVE 区间，而不依赖单一全曲线相似度：
- https://www.mathworks.com/help/fixedpoint/ug/how-the-simulation-data-inspector-tool-compares-time-series-data.html
- https://docs.tracetronic.com/help/ecu.test/2026.3/en/trace.check/Manual/Trace-analysis-design/Calculation-step-and-trigger.html

## 五、现有 R1 需要特别修的 6 个准确性缺口（由源码直接指认）

1. `WaveComparator.cs`：当前所有样本线性插值、按参考点 `Coverage`、`PassRatio`、50ms 默认最佳 shift、末尾超限时长 0 的问题——正式生产分类型/质量修正；
2. `WaveCaptureService.cs`：返回 capture 后只保证至少一个有效样本、`CancelAfter` 与手动 Stop 共用取消路径；需要显式 EndReason/质量闭合，保留异常中断证据，不能让部分记录成为可用生产记录；
3. `LiveDbcSignalService.cs`：只在异步枚举实际消费时注册目标 sink，`OpenAsync` 的 origin 并不等于已接通有效首帧/订阅就绪；生产触发要有明确 `ArmReady` 事件/帧序高水位；
4. `WaveWorkbenchWindow.xaml.cs`：采用单一 `Polyline` 绘制枚举/布尔时显示斜坡（视觉假值），需按语义画阶梯/状态条；`WaveChartAnalysis` 的 display-only 抽稀不能参与判定；
5. `WaveReferenceWorkspaceService.cs` / `WaveReferenceCatalog.cs`：Replay/Simulation 的 CanJudge 可以创建可比较参考，缺真实准入与审批；按 reference name 唯一匹配不等于冻结 ID/Rev/SHA；
6. `RealTestRecordSession.cs` 与 `RunFact.cs`：成功 WaveCompare 使用 `WaveComparisonPendingArchive`，后者 Validate 只接受 `DirectDbcSignal`；对方属于不同类型，必须设计独立受限的正式 Wave 证据，归档后验完成，不能靠降低验证门槛过关。

## 六、让“精度承诺”成为可审查的项目验收件

建议一份正式 `WaveCapabilityQualification`，绑定 `StationProfile/ECUModel/DbcSha/SignalSpec/AlgorithmProfile/SoftwareBuild`：

- **静态数值测试**：DBC 经典/扩展帧、大小端、负数、factor/offset、mux、invalid/quality、边界码，使用人工计算的独立原始字节“真值向量”；未覆盖的不宣称正确；
- **重复性实验**：同一确认合格 ECU、多轮真实动作；不同已知合格 DUT 条件允许时重复；记录该 WAVE 方法的误拒/误判/不可评价率，说明样本数量及局限；
- **时间实验**：用可信外部时间基准（条件允许时，逻辑分析仪/示波器/专用 CAN 记录仪/同步探头）同时观测刺激和 ECU 总线/物理动作，评价 Windows 主机时间延迟、抖动、目标帧周期和事件类型；若没有独立测量，只准出 HostObserved 级；
- **坏例实测**：通过安全合法可恢复的 ECU 状态/工况构造目标值不满足；不要用 mock 修改判定结果冒充实体错误；
- **质量故障注入**：USB 断开、目标 CAN 静默（但其他 ID仍有帧）、缓冲溢出、取消、错 DBC、错 Reference、跨 Run 混证据，全部不能 PASS；
- **全链证据**：校验 `SourceEvidence/RawFrameInRun/StepOccurrence/Reference/Algorithm/ReportSHA`，真实 WAVE PASS 必须经过正式 RECORD；
- **上线回归**：换 R1 源码、DBC、固件、波形算法/Reference/工位设置之后，对受影响的资格项目做明确复验，不能“新软件继续引用旧标准”而不知。

### 建议初版发布等级

| 等级 | 可发布的真实能力 | 禁止宣称 |
| --- | --- | --- |
| `LAB_CAPTURE` | 工具可采集/保存/回放真实 ECU CAN/DBC 信号、质量可见 | 这条曲线证明 ECU 合格 |
| `LAB_JUDGEMENT` | 对已冻结录波和已声明规则有可复算 PASS/FAIL/INCONCLUSIVE/ERROR | 已验证现场时间精度/量产漏检风险 |
| `FIELD_OBSERVED_JUDGEMENT` | 真实 ECU、当前工位、当前 DBC/规则已完成正反和缺帧实测，**只对可观察的 CAN 报告条件**给正式结论 | 未装硬件测时就保证 ECU 物理响应时间或高速电流尖峰 |
| `PHYSICAL_TIMING_CERTIFIED` | 未来必须有独立外部测量或设备同步硬件时间戳及对应受控标定 | 现行 HostArrivalOnly 源码已经具有该等级 |

R1 首版真正的合理目标是 `FIELD_OBSERVED_JUDGEMENT`（条件满足才准入），不是强行承诺 `PHYSICAL_TIMING_CERTIFIED`。

## 七、必须跑的额外准确性对抗向量

| ID | 案例 | 不可违背的预期 |
| --- | --- | --- |
| WA-01 | raw=0xff，DBC signed 8-bit ×0.5−40 | -40.5（示例），显示精度可变但计算原值不变 |
| WA-02 | 切换 DBC SHA，信号名不变、factor改变 | 旧参考/旧 capture 不可静默重算为新 DBC |
| WA-03 | 同 CAN ID 但扩展/标准帧不同 | 必须精确区分 |
| WA-04 | Mux 未选中当前 signal，另一字节恰好等于 target value | 必须视为信号未观测，不计有效点 |
| WA-05 | ECU CAN frame AliveCounter 跳号（如果真实协议提供） | 报文完整性告警，不能用其它信号帧覆盖 |
| WA-06 | 真实 ECU 10Hz，要求保证 5ms 瞬断不存在 | Unsupported / 不允许物理负例 PASS |
| WA-07 | USB 0x41 队列积压但画面显示正常 | 采集质量、抖动/延迟显式可见，不能宣称 ECU 端 1ms 精确度 |
| WA-08 | 同一截取窗口首末无有效目标新帧 | Window incomplete / INCONCLUSIVE |
| WA-09 | enum 0→2 | 阶梯图，不画 1；算法不得把线性 1 当有效 |
| WA-10 | 参考一条真实曲线，实际偏移40ms，规定严格限时20ms，旧 auto-shift50ms | 正式时序不能被归一化成 PASS |
| WA-11 | 数值前1秒1000点、后9秒1点 | 不允许点覆盖率大就代表时间覆盖足够 |
| WA-12 | 最后一采样点失效，后续不再更新 | 不得凭 ZOH 无限延长状态持续合格 |
| WA-13 | Windows UTC 时钟跳变，monotonic保持 | elapsed 以明确 monotonic 时钟域分析，异常记录 |
| WA-14 | 人工提前停止或取消 | 本次记录可保留，但缺必需时间窗不得 PASS |
| WA-15 | 参考来自 replay/仿真 | 分析工具可离线比较，生产参考拒绝 |
| WA-16 | 报告 reopen/软件更新后历史 Ref/Profile改变 | 原有原始 SHA/冻结规则/结果保持可复核，不自动重判 |
| WA-17 | 实际 Wave sample 的某个 EvidenceRef 无当前 Run 对应的 raw CAN bytes | 正式 RECORD PASS 拒绝 |
| WA-18 | 比较 PASS，但 Persist Step 证据类型不被 RunStepFact.Validate 接受 | 精确暴露合同冲突，修复后回归通过才可生产发布 |

测试编制者不得把`模拟测试通过`描述为实 ECU 验收通过；生产数量、误判率/漏判率需足够多的已知真值样本且报告置信度，零次错误不等于错误概率 0。

## 八、最终研究结论

1. **准确度不是比较器的一个参数，而是源字节/解码/时间/窗口/质量/算法/参考/RECORD 八层链式责任**；
2. R1 当前 CAN/DBC 能做专业的主机观察级信号波形工具和有条件的真实自动判定；不能在没有外部证据前宣称同步物理电气时间精度或快速模拟电气尖峰检测能力；
3. 先按 W-09 建成独立真实录波工具，再以本 W-10 的质量合同接入 W-05 的功能判定和 W-06 的正式 RECORD；同一个源与记录，不建第二套采集驱动；
4. 物理时间无法保证/必要时窗缺数据，**正确的产品行为是明确 INCONCLUSIVE/UNSUPPORTED**，而不是松阈值让软件“看起来都通过”；
5. 任何具体阈值、误差上下界和首版质量承诺都需要在用户实际 ECU 上测定，不能拿文档默认值或 NI 的硬件采集指标直接移植。

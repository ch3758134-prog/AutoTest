# W-04｜成熟工业测试产品对标与 AutoTest WAVE 产品架构（第二轮）

**日期**：2026-11-21；**任务**：[Issue #71](https://github.com/ch3758134-prog/AutoTest/issues/71)；**最新源码回读基线**：`R1@e5a9e13ad522ddaf9092f44099f2ef26bdf1e19b`。**状态**：`RESEARCHED / PRODUCT_MODEL_PROPOSED / IMPLEMENTATION_NOT_APPROVED / FIELD_NOT_VERIFIED`。

本文件在 W-00~W-03 的源码调查与合同基础上，进一步回答“成熟工业软件应提供什么波形产品”，而非增加几个默认算法参数。设计仅覆盖 AutoTest **当前真实 CAN/DBC 时间序列**，不声称获得了独立的模拟 ADC、电机相电流探头或者真实 ECU 时钟同步。

## 1. 核心设计结论：产品价值由“可解释的功能判定”而不是曲线相似度决定

WAVE 应服务两个不同但关联的用户任务：

1. **开发/调试/工程确认**：选择已绑定 DBC 信号，真实采集、暂停、放大、双光标测量、保存证据、与参考叠加，帮助工程师研究 ECU 的时序表现。此时**没有强制测试结论**。
2. **TEST 自动判定**：工程师提前指定动作锚点/窗口/期望行为和数据质量条件，执行器获得实时证据后作 `PASS/FAIL/INCONCLUSIVE/ERROR`，交由唯一 RECORD 门禁归档。重点不是“图形很像”，而是**对产品需求是否满足的可靠证明**。

因此，不应默认把“波形”和“参考波形”当作所有测试判定的必需对偶。很多 EPB 功能检验实际上适合**状态转移、响应时限、稳态区间**，参考曲线更多用于电流/压力等形态分析。对于无法可靠观测的短时间变化，产品要明确显示“无法证明”，不能放宽容差掩盖。

## 2. 成熟产品与标准的精确可借鉴点

| 产品/规范（官网文档） | 官网已明确的能力 | AutoTest 应吸收的产品行为 | 不应复制的复杂度 |
| --- | --- | --- | --- |
| NI TestStand | 过程模型与测试序列分层，支持自定义 Waveform Step 类型及将额外结果写入报告 | **波形作为正式 TEST 结果类型**，一个步骤有明确判定值/证据/附件；执行器、结果收集、报告分离；工程师可以直接用例运行 | 生产并行工位、多型号插件架构、完整 TestStand 重建 |
| TraceTronic ecu.test `trace.check` 2026.3 | Trace/Signal/Episode、Trigger Block、CheckResponse、Static/Missing Value、INCONCLUSIVE 子区间、闭合/非闭合端点 | **触发事件 + 评价窗口 + 缺失区间 + 明确结果**；界面显示哪一段不满足或不可评价；不能让最后一个点自动证明整个保持窗口 | 复制 Python 表达式语言、全量 episode DSL、可视化脚本引擎 |
| MathWorks Simulink Data Inspector | 信号配对、union/intersection 时间同步、零阶保持/线性/无插值，绝对/相对/时间容差及上下界可视化 | **按信号语义选择插值策略**；原始采样与参考叠加、误差/容差带；不同信号的容差不可全局一刀切 | 将仿真信号精度/模型时间当作本机 CAN 接收精度；自动容差位移直接作为 ECU 延迟容许 |
| Vector CANalyzer | CAN 帧/信号实时分析，日志、回放、诊断与刺激之间可操作切换 | **同一 CAN 接收数据复用**：DBC 监控、手动台架、波形分析、自动 TEST共享证据；日志与曲线互跳定位 | 重新造一套 CAN 驱动、配置总线网络或第二份原始日志 |
| ASAM MDF v4.3 | 汽车测量数据、信号/元数据、bus events、attachments 与长期测量归档的格式 | 对**来源、时间基准、信号定义、单位、原始帧与附件关联**使用标准化思维；未来可选导出 MF4 | R1 首版强制实现完整 MDF 编码/导入，取代现有校验过的 JSON/RECORD |

官方网站证据及可复核 URL：

- NI TestStand Process Models：https://www.ni.com/docs/en-US/bundle/teststand/page/process-models.html
- NI Creating a Waveform Custom Step Type：https://knowledge.ni.com/KnowledgeArticleDetails?id=kA03q000000x27RCAQ
- NI Report generation：https://www.ni.com/en/support/documentation/supplemental/08/teststand-report-generation-and-customization.html
- ecu.test Trace trigger and calculation：https://docs.tracetronic.com/help/ecu.test/2026.3/en/trace.check/Manual/Trace-analysis-design/Calculation-step-and-trigger.html
- ecu.test Significance of time stamps：https://docs.tracetronic.com/help/ecu.test/2026.3/en/trace.check/Manual/Significance-of-time-stamps.html
- ecu.test Analysis pitfalls：https://docs.tracetronic.com/help/ecu.test/2026.3/en/trace.check/Manual/Hints/Pitfalls.html
- MathWorks Comparison：https://www.mathworks.com/help/signal/ug/how-the-simulation-data-inspector-tool-compares-time-series-data.html
- Vector CANalyzer：https://www.vector.com/en/product/canalyzer/
- ASAM MDF：https://www.asam.net/standards/detail/mdf/

**考察结论**：上述产品的实际通用设计不是一个固定的“波形通过率”，而是**信号/时间模型明确、采集与分析分离、分析与测试结果可追溯、原始数据可回放校验**。这些是产品结构上的借鉴，不代表 AutoTest 已实现相同功能。

## 3. 统一产品模型：四种用途，不创建第二 TEST

建议 `WavePurpose`（在同一个后端能力域、同一项目资源中）：

| 用途 | 用户能做什么 | 参考是否必要 | 可否直接成为 TEST PASS | R1 |
| --- | --- | --- | --- | --- |
| `CaptureAndInspect` | 采集、保存、光标、缩放、叠加、导出诊断证据 | 否 | **否**，仅 Measurement | P0 |
| `StateTransition` | 施加/释放后状态在规定窗口进入目标枚举，并保持 | 否 | **可以**，只在充分观测、时间边界证明时 | P0 最优先 |
| `NumericEnvelope` | 报出的物理量在某时间窗的上下限、稳态、持续超限 | 否 | **可以**，但须证明时间覆盖/失效位/最大采样间隔 | P0 第二 |
| `ReferenceSimilarity` | 与审核通过的特定参考波形逐窗比较、偏差定位 | 是，必须冻结 ID/Revision/SHA | **有条件**，需参考批准、语义插值、时间质量、独立记录门禁 | P0 最小受控 + P1 高级 |

**重要**：前两种功能规则不是“让 Wave 的两个数和现有 AssertSignal 重复”。AssertSignal/WaitUntil 负责某个瞬时/等待条件；WAVE 负责**基于真实采集时间序列的持续时间、过渡过程、质量与证据**。它可复用既有 TEST Step DSL 和 StepOccurrence/R1 RunId，但不能另建 Interpreter，也不能假造一个 waveform-only 出厂记录。

## 4. 工程师完成一个 WAVE 任务的成熟交互

### A. 开始之前——选择可观察来源

从**当前工程 TEST 资源**或波形工具中选择：
- ECU CAN/DBC 绑定信号（完整 Asset ID、Revision、SHA、BusRole、CAN ID/扩展帧、SignalName、Unit）；
- 信号语义：`DiscreteState`、`ContinuousReportedValue`、`Counter`、`EventOnly`；DBC 的数值字段本身**不证明**它可以物理连续插值；
- 期望帧周期（只做经标定的配置，不直接等于 CAN 比特率），最大新鲜度、允许缺测/失效策略；
- 可观察的实际来源：`ECU_CAN_DBC_REPORTED_VALUE`，不写“电机实时相电流”或“示波器电流”。

### B. 采集工作台——专业工具不挤压 TEST 编辑器

一个独立、受主窗口管理的 WAVE 工具窗口，包含：
- 左侧信号/波形证据/参考资产导航，可检索当前工程，不开启多组配置窗口；
- 中央大面积带时间坐标的图，可切换单曲线/实际与参考叠加/容差带/差值曲线/关键触发事件；支持缩放/移动/双光标时间和数值差；
- 顶部明确显示 `来源 ECU CAN、记录/只读状态、当前 DBC 版本、时间质量 HostArrivalOnly、样本数、是否丢帧`；
- 底部/右侧判定详情：`事件锚点、采样覆盖、不能判断区域、失败段、最大误差、响应时界、参考的来源与修订`，而不是满屏状态小字；
- 保存有 `捕获证据` 与 `提交参考候选` 两个不同动作；参考候选批准/冻结是另一明确流程，不能点击一次保存就变成生产参考；
- 查看已归档结果不改变真实采集，不向 DUT 写任何指令。

当前 `WaveWorkbenchWindow.xaml` 其实是“单选证据→单图→双光标测量”的读图器；`WaveWorkbenchViewModel` 有证据枚举和可取消读取/降采样，但没有审核工作流、实时参考比较视图、触发质量轨道和可见异常区间。下一轮 UI 设计只需**在现有专业窗口内演进**，不必再次另造工具壳或将测试编辑器资源树覆盖。

### C. TEST 步骤 Inspector——日常配置路径必须短

采用同一张可见“波形判定”步骤属性分组，不让用户手输 `captureKey`、`referenceKey` 等技术字符串作为常规入口。

1. **信号**：DBC 信号选择器（可搜索/中文别名/单位）+ 引用版本和采样能力。
2. **什么时候采**：前置预采样 `pre-arm`（如需要）、触发锚点（指定已执行 TEST 动作或实际观测的状态）、起止时间窗。
3. **判什么**：`进入目标状态并保持`、`范围内稳定` 或 `与审核参考比较`，显示该判定适合什么类型的信号。
4. **限制**：响应时间上下界/绝对或相对容差/最大间隙/有效持续时间/时间质量最低要求；默认来自明确 Recipe/Profile，没有统一隐藏 5%。
5. **结果和证据**：显示保存 actual、reference、比较段、原始 CAN、StepOccurrence / RunId / 注释的固定要求，普通操作者不应手动设置技术字段。

可以提供“**插入波形测试流程**”模板，帮助用户一次插入并展示现有 `WaveStart → KEY/IGN/CAN 动作 → WaveStop → WaveCompare` 的可编辑步骤树；它不是运行时隐藏添加步骤、不是第二套 DSL，也不能绕过准备/测试/清理。模板插入后每个真实步骤应在主列表可见，右侧 Inspector 常驻编辑，公共流程可以复用。

### D. 正式报告——首先让人知道为什么失败

一次 WaveCompare 报告呈现：
- 标题/判定结果 `PASS/FAIL/INCONCLUSIVE/ERROR` + “为什么”一句清晰中文结论；
- 实际 vs 参考/约束的图、容差带、触发点/判定窗口、异常区间和质量缺口；
- 不隐藏任何导致 inconclusive 的 gap、信号失效、钟源类别或无法核对的样本；
- 指标表：观察点数、有效时间覆盖、最大/中位采样间隔、最大误差、响应时间**区间**、连续越限时间、参考身份/审核、算法/Profile 版本；
- 可展开到底层原始 CAN 证据、DBC 资产、RunId、CaseAttemptId、StepOccurrenceId、附件 SHA 和软件构建 hash；
- 报告可关闭重开；判定不应重新调用用户工作区“当前最新” DBC/Reference，而要使用当时冻结版本和证据。

## 5. 核心决策：触发与时间来源不能隐藏

很多 ECU 操作如下：

`先开启波形监听 → KEY 模拟施加 → 观察 ECU Applied 状态 → 检查时间和保持区间`。

“采集开始”不等于“KEY 实际闭合”；“USB 写指令”不等于“外部电压已经形成”；“CAN 收到新状态”不等于“ECU 内部刚发生此状态”。因此至少有：
- `CaptureArmedUtc/Monotonic`（订阅真正就绪）；
- `StimulusCommandDispatched`（上位机发出动作的证据）；
- 可获得时 `StimulusPhysicallyConfirmed`（硬件确实提供证据，不能凭空编造）；
- `SignalObservedAtHost` 与实际帧 Epoch/SourceEvidence。

当前 USB 0x41 **没有 ECU/MCU 硬件事件时间戳**，因此 R1 最初的时序判定必须限定为**主机观察尺度、充分宽松且经过现场标定的时间界**，不能把它包装成 ECU 端真实电气响应延迟。

## 6. 产品级“可用性”与“可信性”双门槛

- **编辑时可用**：知道怎样选择信号、参考、触发和阈值；操作器能正常采集/看图/导出；不能因为没有准生产参考就阻止工程调试。
- **判定时可信**：来源为本次真实 CAN/DBC、时窗和 trigger 可复算、参考已审、无未报告的丢失、时间质量足够、RECORD 真正写入且报告回读一致。
- UI 可以展示 `样本已采集`，但**不得用绿色 PASS 标识采样成功**；`捕获质量通过` 与 `ECU 产品判定通过` 是两种状态。
- 即使一条真实曲线形状正确，也可能**无法证明**某个 ECU 状态在两帧之间没有短暂故障；这必须成为产品可见的限制而非算法备注。

## 7. 跨部门明确边界

- **WAVE 专项**：规则模型、参考治理、触发/采样质量/比较算法/结果解释、测试向量。
- **R1/TEST 负责人**：唯一 Runner 中步骤生命周期和实际 StepOccurrence/动作事件锚点，PreparedPlan 指纹及运行锁。
- **DEVICE/HW/FW 负责人**：报告实际 USB/CAN 接收时戳、帧拥塞/丢失、会话 epoch；若未来需要精准时间，应另审硬件事件戳方案，本阶段**不改协议**。
- **RECORD 负责人**：新增独立 `VerifiedWaveComparison` 判定证据类型（严守完整性和 occurrence 证明），保留直接 DBC 判定规则。
- **UI 负责人**：基于既有 Fluent 浅色视觉与现有波形工具，实现统一数据语义，不自建参考选取/比较真相逻辑。
- **用户现场验收**：选择真实已知 ECU 状态/报告信号，提供合法工作条件并见证正常、异常和缺测行为。算法单测不能代替现场证据。

## 8. 取舍与建议的首版范围

**P0 必做**：真实信号捕获和诊断显示；单信号明确时窗的离散状态/范围判定；参考来源/唯一修订冻结；现有 WaveCompare 基础算法的 fail-closed 修复；实际+参考+配置+原始 CAN 的 RECORD 链；最小现场正反用例。

**P1 再做**：多通道跨时钟组合、模板化状态机与复杂波形特征、参考上下包络的统计构建、全功能交互分析、通用 MDF 互操作、专业的微秒事件硬件时间戳；这些都不应成为 R1 第一版真实测试的前置条件。

本报告为**技术研究候审**，不表示已完成代码实现、Windows 构建或现场测试。更多判定数学/质量边界见 W-05，参考管理与 RECORD 证据闭环见 W-06。

# W-01｜工业波形判定方法、误判机理与真实 ECU 操作案例

**任务**：Issue #71。**状态**：工业方法研究 + 现有 R1 比较器源码推导；**所有案例均为候选设计，未在真实 ECU 上执行**。源代码快照：`R1@fa3b18e948043a9d1960adddb627658fd40c8159`。

## 1. 可参考的成熟产品方法（只借鉴适合现有硬件的部分）

| 产品/标准 | 公开方法 | AutoTest 的可采纳原则 | **不可照搬** |
| --- | --- | --- | --- |
| [TraceTronic ecu.test：Signal recording / trace analysis](https://docs.tracetronic.com/help/ecu.test/2026.3/en/Tutorial/Fundamentals/Lesson-5-Signalrecording-Traceanalysis.html) | 利用录制信号检查反应时间、持续状态和时间范围内的值 | **动作触发 + 指定分析窗口 + 反应/保持条件** 比整条曲线 RMSE 更符合 ECU 功能测试 | 不能假设 AutoTest 有 ecu.test 所连接的所有采集卡和时钟 |
| [TraceTronic trace.check：Trigger / calculation](https://docs.tracetronic.com/help/ecu.test/2026.3/en/trace.check/Manual/Trace-analysis-design/Calculation-step-and-trigger.html) | 利用触发段指定分析意义和时间边界；边界数据不足可导致 INCONCLUSIVE | 把“窗口可观测性”与“实际动作不满足”分开 | 不复制其完整表达式语言或第二套 TEST DSL |
| [TraceTronic：时间戳语义](https://docs.tracetronic.com/help/ecu.test/2026.3/en/trace.check/Manual/Significance-of-time-stamps.html) | 信号时间戳及记录源之间的时间语义是分析前提 | 明确 `HostArrivalOnly`，禁止把样本时间当成设备真实动作时间 | 无硬件时间戳时不能宣称同步精度 |
| [MathWorks Simulation Data Inspector：数值/时间容差](https://www.mathworks.com/help/simulink/ug/compare-simulation-data.html) | 绝对、相对、时间容差和差异查看 | 数值容限和时间容限分别配置、报告实际差异 | 不把允许时间平移用来消除 ECU 响应延迟 |
| [MathWorks：插值与比较](https://la.mathworks.com/help/simulink/ug/how-the-simulation-data-inspector-tool-compares-time-series-data.html) | 插值规则会影响同步后比较结果 | 显式选择插值模式且为离散状态使用零阶保持 | 不对枚举状态做线性插值，也不在大间隙插值 |
| [NI TestStand：逐步骤结果与报告](https://www.ni.com/en/support/documentation/supplemental/08/teststand-report-generation-and-customization.html) | 测量数值、限制与步骤结果经执行结果链生成报告 | 比对方法、阈值、缺陷区间、证据链**按 StepOccurrence**归档 | 不另建结果引擎 |
| [ASAM MDF：测量数据与离线分析](https://www.asam.net/standards/detail/mdf/) | 记录测量时序数据供离线处理及保存 | 时间轴、原始样本和来源元数据长期保持，画图数据与证据隔离 | 不为 R1 强行增加 MDF 文件格式或额外库 |

**设计取舍**：R1 第一版先实施三类最实用的**信号性质明确**的判定：`DiscreteStateSequence`（状态序列）、`WindowBounds`（窗口内上下限/保持）、`AnalogEnvelope`（确实连续且足够慢的 DBC 上报量参考包络）。其他高级 FFT/相位/动态时间规整（DTW）暂不进入自动工位判定。DTW 可能把“故障响应慢”自动拉齐为“形状相似”，尤须避免。

## 2. 先校验“可观察”，再校验“合格”

**四态定义（研发建议）**：

| 结果 | 语义 | 生产流程 |
| --- | --- | --- |
| `PASS` | 对明示的真实 ECU 输入，在已证明充分的时间/值覆盖内，满足**全部**当前波形断言 | 仅在归档及 RECORD 专项门禁也通过时可纳入运行 PASS |
| `FAIL` | 数据质量足够证明目标 ECU 违反当前版本的已声明约束 | DUT 不合格；保留异常窗口和对应原始 CAN |
| `INCONCLUSIVE` | 数据不足以区分目标是否合格：NoFrame、窗口不完整、延迟/采样不确定度超限、丢帧、无法观察边沿、时钟精度不足等 | 不当 DUT 不合格，也不准 PASS；要求重试/检查 |
| `ERROR` | 参考文件损坏、DBC/参考/配置身份矛盾、算法版本不可用、异常程序/存储失败、输入不合法等 | 阻断运行；修正工程/程序/环境 |

**不能按“有一些异常样本则直接 FAIL”简单定性**。当缺失区间恰好覆盖关键事件且无法确认发生过什么时，不能证明事件未发生，应 INCONCLUSIVE；但若具有完整有效样本且实测明显越过明确的安全/业务范围，则 FAIL。证据是否足够由预先冻结的判据给出，判定不得在看到结果后再自动调整阈值。

质量门禁顺序：`(1) 来源与资源身份 → (2) 捕获完整性、时钟质量 → (3) 观测率/缺失窗口/触发可证 → (4) 时间模型与窗口支撑 → (5) 物理量/状态判定 → (6) 全部样本来源证据归档 → (7) RECORD 最终 PASS 门禁`。若步骤 (1)~(4) 不满足，不进入判定成功分支。

## 3. 时间对齐的工程边界

- `AbsoluteTolerance` 与 `RelativeTolerance` 不是时间误差；`MaximumShiftSeconds` 是沿时间轴移动曲线的自由度，若响应时间也要判定就**不能**使它自由最小化 RMSE。
- 对于动作 `t_action` 与 ECU 信号首次变化 `t_seen`，当前只能观测**主机**接收到 USB/DBC 事件的时间，两者不能自然视为同一时间基准；如果动作仅有“下发/ACK”时间则还要包含物理触发动作延迟。
- 计划在真实台架测量 trigger 观测不确定度 `u_trigger`、目标 CAN/DBC 接收时间不确定度 `u_signal`、参考样本时间不确定度 `u_ref`、报文更新离散误差 `u_period`。保守响应不确定度预算可先取 `U = u_trigger + u_signal + u_ref + u_period`（现场应证实各项是否独立及是否还存在系统偏差；不要虚构数字）。
- 假设某用例明确限值为 `T_max`，测得响应延迟为 `D_obs`：仅在 `D_obs + U <= T_max` 时可证明及时 PASS；仅在 `D_obs - U > T_max` 且事件/端点已充分观察时可证明超时 FAIL；否则 `INCONCLUSIVE`。**示例阈值只是表达合同，不能当全车型标准**。
- 若 `u_*` 未被测得、时钟来源不可兼容，或实际要求精度超出系统能力：`INCONCLUSIVE/TIME_CAPABILITY_NOT_PROVEN`；不得用“默认 50 ms 移位”来假定能力。
- 比较“波形形状但**不**检验延迟”时可在工程师审批后设置有界触发对齐；`AlignmentMode=ExplicitTrigger` 且记录触发证据，禁止无条件 RMSE 最优平移；`AlignmentMode=NoShift` 是建议默认。
- 波形窗口必须含事前 `pre-trigger`、动作与过渡、事后 `post-trigger` 观测。禁止基于两条曲线各自“第一个样本为 0”自动忽略真实响应时间差。

## 4. 数值、状态和采样的不同模型

### 4.1 DBC 离散状态 / Bool / Enum

- 零阶保持（`ZeroOrderHold`）仅在已知最大允许间隔 `MaxHoldAge` 内有效；过了最后一条有效报文的期限即为未知而不是无限保持。
- 比较**合法状态、状态顺序、首次达到状态时刻、稳定保持窗口和禁止状态**，优先于逐点 RMSE。
- 如果状态变更发生在报文两次采集之间，实际“事件发生时刻”只能定位到区间 `[t_prev, t_next]`；区间与阈值重叠时判不充分，不能靠直线内插缩小这个区间。
- 若报文是否周期发送未知，必须先标定状态信号更新契约；不能仅用 CAN 总线里“有报文”视为对应信号有效。

### 4.2 CAN 上报的连续数值（例如 ECU 电机电流估值）

- 只有当 DBC 表明信号是合理的连续数值且采样率/时间分辨率合格时，才可使用**受限**线性插值；禁止跨过 `MaximumInterpolationGap`、source lost、会话重连与信号错误。
- 比较参照包络和最大偏差，还要限定平台 `minimumDuration`、`maxOvershoot`、`minimumHoldDuration`；不能只看平均误差或均方根误差。
- 如果 MCU/ECU 的上报数字值会饱和、滤波、裁剪或只发状态摘要，判定物理意义必须以 ECU/DBC 文档为证；不能暗示能看到实际电流尖峰。

### 4.3 异步、非均匀采样

- 先用参考和实测原始 `TimePoint` 统计间隔 `min / median / p95 / p99 / max`，检验显式 gap/loss/overflow/epoch。
- 覆盖率分为 **采样点覆盖率** 与 **有效时间覆盖率**；用于生产门禁的是后者加上每个关键窗口的硬约束，不允许密集采样段掩盖稀疏或丢失段。
- 对非均匀样本统计误差，需按实际持续时间加权并保持每段 `maxAbsError`、`longestOutsideInterval`、`lastWindowViolation`；尾端没有下一观测不能假装超限时长 0。
- 可视化 min/max 采样压缩绝不能进入判定算法，必须保留原始序列与证据 ID。

## 5. 三组 ECU 波形判定操作样例（全部需真实 DBC 最终绑定）

以下 `IGN`、`MotorCurrent`、`EpbState`、`EpbMotorCommand` 只是**示例信号语义名称**，不代表实际 ECU DBC 已含这些精确字段。最终必须由工程师在真实 DBC 中选择合法名称、单位和帧 ID。

### A. EPB 施加动作后状态阶跃

**目标**：在有效工作条件下执行 EPB 施加命令后，`EpbState` 由 `Released` → `Applying`（可能存在）→ `Applied`，并保持 `Applied`。实际用例必须由真实需求确定是否允许中间态、超时与保持时长。

操作：打开工程和当前 DBC；选择 ECU CAN 信号 → 取得由真实有效 DUT 操作确认的参考 → 关联本次 `KEY_SIM`/IGN 动作触发事件与证据 → 提前开始捕获 + 保留足够的前后窗口 → 用 `StateSequence + TransitionDeadline + StableHold` 判定 → 对报告输出每段状态、收到帧证据与最早/最晚可能转换时间。

| 情况 | 应得到 | 为什么 |
| --- | --- | --- |
| 真实状态按预期转换、全窗口有效、时延裕量足够 | PASS | 顺序和保持条件均被证实 |
| 所有目标报文均有效、超过上限仍维持 `Released`、不确定度已界定 | FAIL | 可证伪目标动作响应 |
| 临近时限才收到 `Applied`，边界区间跨越时限 | INCONCLUSIVE | 不能精确界定是否按时 |
| 丢了包含唯一跳变的报文或有失效/断连 | INCONCLUSIVE | 未观察到并不等于未发生 |
| 以 ±50 ms 对齐后看似与参考相同 | **不得直接 PASS** | 可能掩盖响应慢；以触发时刻计算真实响应 |

**现有算法风险**：`WaveComparator` 会线性插值状态编码和自动位移，不够判断目标序列。

### B. ECU 上报的电机电流曲线（仅 CAN 数值，非外部电流探头）

**目标**：施加/释放操作后，来自 ECU DBC 的 `MotorCurrent` 在过渡窗、稳态窗符合工程授权的限值和参考形状。电流是否可用于功能判定需依据 ECU 实际定义（滤波/饱和/更新频率）。

操作：绑定原始值单位/Factor/Offset 对应的 DBC 修订 → 采集真实合格样件并由工程师明确审核保存参考，带硬件/固件信息 → 当前 ECU 实际捕获 → 选择 `AnalogEnvelope` 并声明 `MaximumGap`、`BoundedInterpolation`、平台区间/峰值/持续时长/稳态噪声限值 → 归档原始 CAN、DBC SHA、参考 SHA、越差时段、报告。

| 情况 | 应得到 | 误判防线 |
| --- | --- | --- |
| 有足够时间覆盖、相对/绝对误差均在允许包络，峰值和稳态条件都满足 | PASS | 多条件同时要求 |
| 真实平台电流长期偏高且有持续有效证据 | FAIL | 不能被整体 RMSE 稀释 |
| 峰值仅有一两个上报点且中间采样周期远大于可允许的峰宽 | INCONCLUSIVE | 信号更新率不足 |
| 参考从旧 ECU/错 DBC 单位导入 | ERROR | 参考/DBC/ECU 条件失配 |
| 只在报告看到“曲线平滑” | **不可判** | 图形观感不是物理证据 |

**注意**：当前 R1 没有外部采样模块支持高带宽电机相电流波形，因此不能以“参考曲线形状相近”证明真实瞬态峰值、纹波或电气边沿达标。

### C. IGN 或控制状态撤销之后 ECU 状态恢复与保持

**目标**：从预定义状态切换 IGN 或其它经过授权的控制量后，在一个确认的观察窗口内，某个 ECU 上报状态进入允许模式并保持，不出现禁用状态。注意如果 IGN 关闭导致 ECU 停止发报文，应按该 ECU 的明确通讯规范定义：**NoFrame 可能是目标本身，也可能是总线/电源/USB 故障**；不能未区分即认定 PASS。

操作：预检 ECU CAN 已被监听且目标信号有效 → 记录控制动作来源/真实时间证据 → 声明 `ExpectedSilence`（如需求允许）和独立总线健康心跳作为可观测性证明 → 检查目标状态/沉默的定义与持续长度 → 对可能的总线丢失给 `INCONCLUSIVE`。

| 情况 | 应得到 | 原因 |
| --- | --- | --- |
| 状态明确达到，持续有效且总线健康 | PASS | 直接状态证明 |
| 明确反向状态持续并越过上限 | FAIL | DUT 行为确实违约 |
| 仅观察到目标报文缺失、无独立总线健康证明 | INCONCLUSIVE | 不能把故障或掉线当成正常停报 |
| 参考与本次 IGN 状态/产品型号不一致 | ERROR / 配置阻断 | 测试条件与参考不兼容 |

## 6. 按源码对抗性测试设计（离线算法测试 ≠ 现场验收）

| ID | 构造 / 真实现场注入 | 当前典型风险 | 目标分类 |
| --- | --- | --- | --- |
| W-A01 | 相同数值序列，无缺失，无需移动的同触发测试 | 正常 | PASS |
| W-A02 | 实际整体响应延迟，但移位后形状吻合 | **假 PASS** | 迟滞超过限值应 FAIL；时钟精度不够 INCONCLUSIVE |
| W-A03 | 阶跃状态 0→2，中间无状态 1，参照中期望 1 | 插值造假 | 离散状态须按合法状态序列判 |
| W-A04 | 只有前 95% 窗口有样本，末端故障缺失 | 假 PASS | INCONCLUSIVE |
| W-A05 | 参考密集、实际稀疏，两点跨跃过尖峰 | 假 PASS | INCONCLUSIVE / 不支持目标频宽 |
| W-A06 | 实际最后一个样本明显越差，无后续点 | `OutsideDuration` 末端低估 | FAIL（明确幅值越界规则）或 INCONCLUSIVE（无法证明持续时间） |
| W-A07 | 参考/实际采样非均匀，最坏区间稀疏 | 按点通过率偏置 | 依有效时间覆盖与区间限值 |
| W-A08 | 数值恰在 ±容差边界且时间不确定度跨阈值 | 仅浮点比较产生误判 | 时间判断 INCONCLUSIVE，值边界采用冻结精度规则 |
| W-A09 | 单次来源 USB RX queue/Hub/signal sink 溢出 | 缺失传播不充分 | INCONCLUSIVE |
| W-A10 | 捕获中 USB 重连或 DBC 编辑换版 | 使用旧/新数据拼接 | INCONCLUSIVE / ERROR |
| W-A11 | Replay/Simulation 作为参考被“人工确认” | 假真实生产参考 | 生产引用 ERROR；实验可分析但不得认证 |
| W-A12 | 参考名相同但文件 ID/Revision/SHA 换了 | 隐式换基准 | ERROR（运行前冻结资产不匹配） |
| W-A13 | 同一 StepId 在 Loop 两次调用相同 captureKey | 第二次捕获与第一次混淆或异常 | 两个独立 Occurrence/Capture ID，不混用 |
| W-A14 | 波形 Compare 先 PASS、归档 Wave JSON 丢失 | 报告伪 PASS | 正式 Run 不得 PASS |
| W-A15 | 波形 Compare PASS 但对应的原始 CAN evidence ID 不在归档 | 伪造/孤儿证据 | ERROR/INCONCLUSIVE，正式 Run 不得 PASS |
| W-A16 | 未测得 `u_trigger/u_signal`，却要验收短响应时限 | 假精度 | INCONCLUSIVE |
| W-A17 | 对异常尖峰做绘图降采样，显示看不出来 | 显示误导 | 判定始终基于完整波形，不依据像素 |
| W-A18 | CAN 信号状态停止上报，静默本身被误判 PASS | 伪状态 | 无健康佐证 INCONCLUSIVE |

**离线强制断言**：每组保留输入样本、时间戳、来源标识、比较配置、算法版本、预期 `Verdict + Code`；对旧算法与新算法的差异出具 `before/after` 解释。不允许只跑“同形通过”和“幅值很大失败”两条常规测试。

## 7. 阈值标定与参考波形治理

不能全项目默认 `5%` 或固定 `50 ms`。阈值来源建议优先级：

1. ECU 需求/工艺规范：功能上下限、超时时限、最短保持与禁止状态。
2. DUT 合格样本横向/纵向重复采集：不同台架负载、温度、电源、固件版本；记录统计与不确定度。若只有一台合格样件，不能称它是产品容差的统计依据。
3. 与物理故障样本分离度：在**真实故障受控注入**或已知不合格 ECU 下确认可拒绝。
4. 采集可观测性：报文上报间隔 p99、时钟 jitter、USB drop 和连续窗口；阈值不可窄于系统实际可证明的精度。
5. 冻结版本：每一次调整参考、阈值或算法都是工程资源新修订，不可在运行中随结果自动修改。

参考波形必须保留：`Project/ECU model / ECU hardware&firmware / DUT qualification / DBC asset id-rev-sha / signal identity / capture provenance / time quality / reference revision&sha / approved by&date / acquisition conditions`。模拟、回放、导入的非物理来源可用于算法研发，但标记 `LAB_ONLY` 且永远不授权 `PRODUCTION_REFERENCE`，除非由可审查的真实测量原始文件和审批流程另行证明。

## 8. 现场前置研究问题（先做标定，不要求用户立即回答）

- 现有被测 EPB ECU 的实际 DBC 哪些信号有周期更新、更新率和状态类型？哪些电流值是 ECU 内部测量/滤波，哪些仅为目标值？
- 哪一个动作可以形成**可证明起点**的触发？主控 KEY 动作的 USB ACK 不等于 ECU 物理动作；是否可通过受观察 CAN/反馈信号定义 trigger？
- 当前总线波特率、USB 排队、CPU 负载下，最大接收延迟与丢帧分布是多少？是否有比主机接收更准确的时间来源？
- 工艺要求的“延迟”是 0→1 的第一帧变化、稳定进入目标状态，还是保持不再抖动？这三种要求不能互换。
- 哪些生产错误应阻断工位并提示人工复测（无数据/资源版本不合/硬件掉线），哪些可判 DUT FAIL（可证明违反要求）？

## 9. 结论

WAVE 正式生产判定是**有来源证明的时间约束断言**，不是把实测与参考线在界面上拉到一起看起来相似。R1 优先交付：**真实单信号、触发窗口、零阶/受限插值、按工程约束的 PASS/FAIL/INCONCLUSIVE/ERROR、同 Occurrence 原始 CAN/参考/比较档案与报告闭环**。高级多信号同步、时频分析和完整工具 UI 留待在真实 ECU 验证后扩展。

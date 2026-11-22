# W-11｜根据 2026-11-21 最新产品管理澄清全面重整 WAVE

> **专项**：WAVE / Issue #71 · **状态**：`USER_BASELINE_RECONCILED / DESIGN_PROPOSED / SOURCE_GAPS_VERIFIED / WINDOWS_NOT_RUN / FIELD_NOT_VERIFIED`
>
> **本次读取的实际基线**：
> - 产品管理权威：`Project_Management@6d2a476de8eba752a227a02dc77b0fc5d30296aa`；`docs/project-management/current/README.md` 明确取代上一版 `current-20261121/`；
> - 活跃产品源码：`R1@5f7a2cf42c1a30434431af18e2fb26741075ad2f`；该 HEAD 相比前次 `8e6aaa556...` 又增加 17 个主要 SCAN/FLASH 配方提交，**没有触动 WAVE 核心源码**；
> - 本研究此前最新 HEAD：`research/r1-wave-judgement-20261120@6d3c0a48049600f0405557a3195643eb547e0fce`；
> - 公开对标：MathWorks Simulation Data Inspector 多子图/多信号和比较插值策略、TraceTronic ecu.test trace.check 多信号非同步时刻/触发/不可评价、NI 采集-日志-UI 分离。
>
> **权限边界**：研究结论写入 WAVE 研究分支，不改写 `Project_Management` 的用户确认决定，不直接修改 `R1/main` 或固件。本文是产品合同适配研究，不代表已执行 Windows Build 或 ECU 现场测试。

## 1. 权威规则与旧设计取代

优先来源：
- [现行项目管理入口](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current/README.md)
- [决策 D01~D18 及历史取代台账](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current/00_DECISIONS_AND_SUPERSESSION.md)
- [01 产品、工具、资源职责](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current/01_PRODUCT_SYSTEM.md)
- [02 一个 ECU 型号一个工程和有序 TEST](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current/02_ECU_PROJECT_TEST.md)
- [04 原始证据、日志、单台与批量报告](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current/04_RECORD_LOG_REPORT.md)
- [05 硬件能力边界](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current/05_HARDWARE_AND_RESOURCES.md)
- [06 最新 R1 GAP/验收](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current/06_R1_GAP_AND_ACCEPTANCE.md)
- [07 WAVE×TEST×RECORD 专项交接](https://github.com/ch3758134-prog/AutoTest/blob/Project_Management/docs/project-management/current/07_SPECIALIST_WORKPACKAGES.md)

**明确的用户产品合同：**
1. `WAVE 是可独立完成工作的专业工具；即使不打开工程也应可用`。独立窗口属于主程序工具生命周期管理，但不是 TEST 的子页面或附属执行环节。
2. `DBC、波形录制/参考、测试规则是可复用资源`。工程按 ECU 型号组织、使用资源；**资源不属于 TEST 或 Production 私产**。
3. 一个工程绑定一个 ECU 型号、一套当前有效 DBC、一个有序 TEST 用例树以及当前固件/生产烧录配置；不额外发明多层 Recipe/车型变型/每步生产独立波形配置。
4. WAVE 工具支持**多个真实 CAN/DBC 信号同时显示、采集、分析、参考采集**；一个信号比较算法不等于完整工具。
5. 工具自由调试不要求生产扫码、不强制载入当前工程文件、不偷偷修改工程。**将工具的独立参考显式纳入工程**时应拷贝/固定资产版本与依赖。
6. TEST 在现有 Prepare/Test/Cleanup+执行器中引用工程资源，运行时冻结具体版本/算法/参考/参数；生产直接运行工程树的全部已启用用例，不另建专门 Wave Engine 或“生产波形步骤配置”。
7. 原始 CAN 为通信底本；WAVE 和测试步骤提取的关键测量进入测试日志与简洁单台报告，失败/停止与不确定亦存证；报告与日志有不同呈现职责。
8. 测量准确度、时钟能力、现实硬件观测边界和正式证据的缺口仍存在；不能在静态源码研究中宣布真实测量准确或 PASS。

## 2. 此前 W-00~W-10 的具体裁决：保留、修订、取代

| 旧研究/假设 | 新状态 | WAVE 的新合同 |
| --- | --- | --- |
| W-00/W-05/W-10：真实 ECU CAN 0x41 = HostArrivalOnly，DBC 时间序列≠外部示波器 | **保留；物理硬限制** | 用户不因为工具独立而获得新 ADC/同步时钟；真实准确度仍需分数值、时窗、缺帧、判定、来源 |
| W-00/W-06：WaveCompare RECORD 证据型别冲突、原始源证据/Occurrence 链不足 | **保留；P0 阻断** | 独立波形判定证据类型/源归属，不伪装 DirectDbcSignal；遵从唯一 RECORD |
| W-01/W-04：状态转移/数值区间/参考比较分不同方法 | **保留；设计提案而非用户已批准公式** | TEST/WAVE 复用同一分析模块；算法严格依据语义和时间精度 |
| W-02/W-06：捕获、批准参考、固定 Reference ID/rev/SHA 的分层 | **保留原理，简化交互** | 工具可随时保存普通录波；正式工程参考须确认来源/可用范围并显式发布；不要求所有工程调试操作走重审批 |
| W-03/W-07：先做单信号 P0 验证，之后才考虑多信号 | **仅作为内部算法最小纵向切片；不能是 WAVE 工具验收终态** | 用户已确认多信号同屏，专业工具 R1 交互验收应覆盖多条真实信号；算法首个闭环仍可用单信号 |
| W-04/W-09：波形工具“从当前工程选择 DBC/证据”的默认唯一入口 | **已被 D02/D03 取代** | 工具同时支持`无工程 / 独立 DBC`和`主动选用当前工程 DBC`；不应将 ProjectWorkspace.Current 当工具前置门槛 |
| W-04/W-09：工程配置/参考建立可能等于工具内部录波资源路径 | **修订** | 先独立录制到工具的受控本地资源/用户选择位置；用户主动“纳入当前工程”时才生成/复制受控工程资产和依赖，避免暗改工程 |
| W-04~W-10：工具内“临时 Compare”与 TEST 中 WaveCompare | **保留但明确权责** | 工具是可解释的工程分析，可按可评价性显示诊断结论；**不凭工具比较结果发布生产合格** |
| W-07：特定 P0 波形步骤必须靠 Reference | **修订** | 状态/数值规则可直接引用冻结的判据而**不要求参考曲线**；只有 ReferenceSimilarity 必需已发布适用参考 |
| W-09：多轨采集可能意味着多套独立 WaveCaptureService | **不采纳** | 一次真实多信号录制会话（CaptureGroup）组织多路异步 SignalSeries，复用**一个共享真实 CAN 接收源**；不是多台模拟采样仪器 |
| W-09：专业工具中的物理动作通过现有台架/TEST 来完成 | **保留** | 可以并行打开多个窗口观察，但物理写控制依设备会话及所有权，不强制关闭工具或私自抢写 |
| 所有 W-00~W-10 的精度/审查/参考候审阈值 | **继续研究，未获自动批准** | 产品合同已确认；Profile Schema、触发语义、时限、阈值和 RELEASE Gate 需结合实际 ECU/TEST/RECORD 评审 |

历史文件保留原研究出处，不偷偷修改它们当时的结论；以后以**本 W-11 + W-12** 解释最新业务要求和实现依赖。

## 3. 四个角色的真正关系（产品域模型）

`
                  ┌───────────────────────────────────────┐
                  │       通用资源与硬件/数据事实          │
                  │   DBC、录波、参考、规则、CAN/USB       │
                  └────────────┬───────────┬──────────────┘
                               │复用       │复用
       ┌───────────────────────▼─┐       ┌─▼──────────────────────┐
       │     WAVE 专业工具        │       │     工程师 TEST         │
       │  无工程录波/多信号/参考  │       │  编写/运行冻结波形判据  │
       └─────────────────────────┘       └───────────▲────────────┘
                                                      │同一 TEST
                                         ┌────────────┴────────────┐
                                         │     独立生产工作区       │
                                         │  执行当前工程全部启用用例│
                                         └─────────────────────────┘
`

注意：Production **不是 WAVE 的父实体**。工具保存的记录并非默认生产 Inspection；普通研发录制可有工具级 CaptureSessionId，但无生产 RunId/StepOccurrence 不得编造它们。正式 TEST 的 WAVE 采集必须补齐真实 Run/StepOccurrence/Device epoch 和 RECORD 合同。**资源被工具保存和被工程发布，是两个不同的明确动作**。

## 4. 三种操作环境，严守差异

### A. 无工程打开专业 WAVE

路径：`工具 → 波形 → 接主控真实设备 → 打开本地 DBC（或使用仍明确加载的独立资源） → 添加多个已解码信号 → Live → Start Recording → Stop → 保存实际录波 → 观察/诊断 → 建立独立参考候选`。

- **不要求**：ProjectWorkspace.Current、扫码、TEST 映射、TestProject、生产 FlashProfile、Recipe、工程资源修订。
- DBC 的外部文件 bytes 要在捕获时快照/hash，以便之后重开或纳入工程；工具可显示“无工程·独立文件”，不暗改任何工程。
- 没加载合适 DBC 的情况下，可以显示**真实原始 CAN 事件/数据概览**，但不能虚构具有工程单位的 DBC 信号数值曲线。选定正确 DBC 才能解码并产生相应 SignalRef。
- 无工程的录波有自己可持久访问的本地路径与来源/完整性清单（实际目录策略待 UX/PROJECT 技术评审，用户无需自建工程）；在测试系统发生故障时不能不经确认丢弃记录。
- 离线录波/导入可供工具分析，Replay/Simulation 不自动提升为真实生产参考。

### B. 已打开 ECU 工程，通过专业工具工作

路径：`工具 → 波形 → 使用工程当前有效 DBC（默认建议、非强制） → 多信号采集 → 保存录波 → 选择纳入工程/建立参考候选 → 审查/批准适用范围 → 工程资源显示该参考`。

- **“本次工具临时会话”与“正在编辑当前工程资源”必须显式标识**；独立选择另一 DBC 不能静默替换工程唯一有效 DBC。
- 工程只需一套当前有效 DBC；可以有多个有意义的参考/波形规则资产供 TEST 用例调用，不应额外创造“生产选哪套参考”的第二层配置。
- 导入/纳入工程时复制/冻结实际 reference 和 DBC 依赖，避免原文件挪动/更新使后续生产失效；不同时篡改运行中的准备计划。
- 工程常规编辑自动保存；非法草稿能恢复，但不能作为正式可执行 Reference；取消或失败不覆写旧 approved 文件。

### C. TEST/Production 正式执行 WAVE

`TEST Step selects Signal(s) + Criterion (+Reference if required) + Timing/Quality policy → PreparedPlan pins DBC/Signal/Ref/Algorithm/Thresholds + action windows → RealRunCoordinator executes physical actions/records actual wave → WAVE pure analysis returns PASS/FAIL/INCONCLUSIVE/ERROR with source evidence → RECORD attaches Wave evidence under real Run/CaseAttempt/StepOccurrence → report/inspection.`

- 开发运行单步/用例/勾选范围；生产执行工程已启用测试树全部实际用例，**不随工具临时选信号、参考或配置自动变化**。
- 不另加“生产波形配置页/Production WaveRecipe”，不用每台扫码时单独选择参考；依 TEST 的已冻结引用执行。
- 生产**前置烧录**是工位级阶段，属于新管理流程；WAVE 不能再把烧录设计成波形测试前的额外 TEST Flash 必选步骤。
- 工具可以只读查看当前真实采集（在有效设备授权和数据质量不受影响的条件下），不能改变生产判定/实际记录。

## 5. 最新 R1 源码差异矩阵（静态确认，非 Windows/现场）

| 路径/组件 | 当前已核实行为 | 新合同需要 |
| --- | --- | --- |
| `Desktop/Shell/MainWindowViewModel.OpenWaveWorkbench` | 无工程提示“请先打开工程”；`WaveWorkbenchViewModel` 依赖 `ProjectWorkspaceService` | 无工程能启动、有项目时可选择工程/独立资源 |
| `Desktop/Shell/WaveWorkbenchWindow.xaml(.cs)` | 仅一条所选已存曲线 `Polyline` + 双光标；没有实时多信号工作台 | 多信号叠加/分轨/共用时间游标 + 真正 Live/Start/Stop/触发/参考、按离散状态用阶梯线 |
| `Desktop/Shell/WaveWorkbenchViewModel` | `Items` 为工程 `wave-records` 文件和参考；`Series/Points` 单条；异步读图 | 独立+工程双资源来源，多信号选中状态、共享视窗/采集组/质量、无工程证据目录 |
| `Desktop/Hardware/LiveDbcSignalService` | `BindAsync` 需要工程，`OpenAsync` 只接受已绑定到 TEST 的 SignalRef；复用真实 CanHub | 通用多信号采集适配层，支持独立快照 DBC 与当前工程快照，保持唯一解码/原始证据链 |
| `Specialists/Wave/WaveCaptureService` | 单 SignalRef/会话；有最多 16 session 槽、有限缓存与 Stop/Compare | 多信号 CaptureGroup 时窗/epoch/触发与逐信号质量；不以16个独立捕获伪装硬件同步 |
| `Specialists/Wave/WaveComparator` | 统一线性插值、按点 coverage、默认50ms shift | 分离状态/数值/参考算法、质量先于判定、是否允许移位明确可控 |
| `Desktop/Configuration/WaveReferenceWorkspaceService` + `WaveReferenceCatalog` | 只能扫描当前工程的 evidence/reference；`CanJudge` 允许 Replay 作为可比较候选 | 独立存储与纳入工程路径；区分捕获/候选/生产参考的来源/状态，ID/Rev/SHA 冻结 |
| `Desktop/Testing/ConfiguredWavePort` | 构造强制 Current Project；`captureKey` 全局字符串、单目标 | **保留为正式 TEST adapter**；不要把其项目依赖当底层通用 WAVE 的限制；真实 Occurrence 分组 |
| `Application/TestRecord/RealTestRecordSession` & `Records/Model/RunFact` | WaveCompare PASS 用 `WaveComparisonPendingArchive`，验证只接收 `DirectDbcSignal`，存在入库合同冲突 | 独立的有限 Wave evidence 验证/归档已确认后正式 PASS，不能删光验证 |
| `WaveChartAnalysis` | display-only 下采样、只处理单 Series | 保留仅用于显示原则；每信号按视窗极值保留/同时间轴/不同单位分轨，判定不读绘图点 |
| `TEST/RealRunCoordinator` | WaveStart/Stop/Compare 已接用例执行，单次读取配置 | 沿用唯一执行器，冻结多信号/参考与触发后证据，不能建立第二 TEST |

**新增 R1 17 个提交影响**：按 GitHub compare，本轮与 `8e6aaa556...` 的差异主要是 SCAN/FLASH Recipe 和某些 Shell/USB 安全规则，没有任何改动上述 WAVE 核心代码。此前源风险仍需定点修正，而不能因为 R1 HEAD 增长就宣布已修复。

## 6. 清晰的需求确认与设计提案划界

**已确认用户要求**：
- WAVE 工具无工程、独立、可选资源；
- 多信号实时观察与采集、参考采集作为工具能力；
- 工程可使用/保存参考，TEST 引用做正式判定；
- 波形实际精度与误判须研究证明，不能用模拟结果冒充；
- 使用统一底层 CAN/DBC/TEST/RECORD、现代浅色 GUI 和无冗余弹窗；
- 生产执行工程所有 Enabled 用例、不能重新配置另一份生产波形规则。

**本专项建议而尚未获产品冻结的实现细节**：
- CaptureGroupId / WaveCaptureSet / multiseries schema/预触发环形缓存；
- 各信号 MaxAge/Gap 或同步误差预算，以及 Count/threshold 具体数字；
- 单独存储目录默认位置、独立录波纳入工程资源的具体导入 UI；
- 参考轻量工程审核状态和跨信号联合判定的正式 Schema；
- 状态时间/数值包络比较器的具体边界、阈值与性能预算。

不能因为文档写得详细便将建议写入管理为 USER_APPROVED。关于真实 ECU 的精度上限和硬件时戳，现场数据仍为必需条件。

## 7. 最小落地策略（产品一致且便于实测）

建议以两个可独立验收、共享核心的纵向链路推进：

**Tool MVP（必须是工具，不是 TEST 变体）**：
`NoProject Open → Select External DBC/Project DBC → select ≥2 real signal tracks → Live/Record/Stop → quality & raw CAN details → save/reopen → reference candidate → explicit project import`。

**TEST/Record MVP（单信号先证明完整，之后扩多信号规则）**：
`Freeze one real DBC signal & vetted rule/ref → WaveStart/physical action/WaveStop/WaveCompare → PASS/FAIL/INCONCLUSIVE/ERROR → Record StepOccurrence/SourceRawCAN/ReferenceSha/Report`。

独立工具多轨是正式用户体验要求，单信号先行只是后端自检切片，**不可将其当最终产品交付**。两个路径可分工、并行实施，但要共享 WaveCapture/DBC/Time/RawEvidence/Reference 真相，不要求用户在工具里先建测试程序。

## 8. 后续 WAVE/TEST/RECORD/UI 专项责任

- WAVE：定义 source-neutral 捕获/多信号/参考/quality + pure comparator，保留当前有价值的 service/repository；实现前给最小增量合同。
- DEVICE/DBC：提供唯一真正的 ECU CAN 源、可选工程/外部 DBC 只读解码快照和每级 drop/epoch 质量，不由 WAVE 重新开 USB 或重复解码。
- PROJECT：定义独立资源→工程资源的明确复制/版本冻结、单型号唯一当前 DBC、草稿/自动保存恢复。
- TEST：只对正式运行的波形拿实 RunId/StepOccurrence 和 action trigger，编译时冻结、重入/循环正确归属。
- RECORD：通过正式 Wave judgement evidence/Raw CAN source correlation/HTML 报告实现归档，但不重建第二日志库。
- UI：主窗生命周期、专业弹窗多轨及引用状态、工程/独立模式清晰、同一 Fluent 浅色；R1 仍需 Window 与硬件实测。
- 用户/现场：选择真实 HydEPB 目标信号/状态条件，逐步实测数值/时间/丢帧/参考资格，不在没有硬件证据时强制发布数值精度声明。

**研究状态：没有 HOST 源码更改、没有 Windows 构建、没有真机采样/准确性认证。**

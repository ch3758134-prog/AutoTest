# W-12｜无工程可用的多轨波形工具、共享采集内核与 TEST 判定的实施/准确度合同

> 日期：2026-11-21（根据当晚最新 `Project_Management/current/` 复核）；Issue #71。**研究建议，须 WAVE/DEVICE/PROJECT/TEST/RECORD/UI 联合审核后实施**。对应 R1 源码快照 `5f7a2cf42c1a30434431af18e2fb26741075ad2f`。**没有** Windows 编译、USB 真录波/多轨测量实测、生产放行结果。

## 1. 产品操作流程：不以 TEST 为工具入口

### 1.1 无工程：最短实际操作

`启动软件（可不打开工程） → 工具/波形 → 查看设备连接 → [打开独立 DBC 文件] → 搜索/勾选多条 ECU CAN 信号 → 实时监视 → 开始记录（先明确订阅就绪） → 在手动台架或真实 ECU 上操作 → 停止并保存 → 光标/多轨/原始 CAN 查看 → [存为普通波形、候选参考、导出]`。

- **无工程工具不得弹“请先打开工程”**。本地工具录波的受控可恢复存储与工程资产存储是两种**显式**资源上下文；具体默认目录、清理/导出策略待 PROJECT/UI 评审。
- 不要求先选择测试程序或 TEST target mapping。选择 DBC 为了将真实 CAN bytes 解释为信号；若未加载 DBC，可查看真实 CAN 帧流但**不可凭空产生带物理单位的信号**。
- “打开 DBC”只建立工具本次所选**只读解码快照**；不在后台悄悄修改有工程时的当前有效 DBC/生产配置。改变 DBC 后已录制数据仍以当时 SHA/版本解释。
- 专业工具是独立窗口，不需关闭正在打开的测试编辑器；但物理写权限来自唯一 DEVICE 会话。可只读同时观察，Flash/硬件独占期间按真实质量/连接事实暂停或结束录制，不把前一会话样本继续拼上去。

### 1.2 有工程：重用资源，而非互相从属

`打开工程 → 工具/波形 → 使用当前工程有效 DBC（建议默认、可切独立 DBC） → 选信号/多轨 → 录制/比较 → 保存独立录波 → [明确“纳入当前工程”] → 工程参考候选/确认/发布 → TEST Inspector 选择工程已确认参考/判据`。

- 工程一个型号/一个当前有效 DBC，不意味着一个工程只能存在**一条**录波或一条参考。可以按实际 ECU 测试用途保存多条参考资产，但不要强迫用户维护多个 TestProject/Recipe/生产“波形方案”选择器。
- 有工程时显示“使用工程 DBC rN”和“独立 DBC 文件”来源，警示改资源时影响；工程编辑自动保存，但**不能影响当前已冻结运行计划**。
- 记录中存入真实 DBC bytes SHA + Signal Name/Frame ID/Extended/Mux/Unit + Input RawCAN Evidence + Frame timing provenance；工程导入时持久化必要依赖或拒绝不匹配。历史原记录不被重写/不从最新 DBC 重新解码。
- 普通开发波形不需要复杂审核，每次录制可直接保存；正式出厂判断用参考，须有能证明来源、适用性、修订 SHA 的独立正式确认过程，流程简短。

## 2. 专业工具界面合同：多个信号是第一优先级

建议一个浅色 Fluent 工具窗（主窗口持有，退出时统一关闭）：

| 区域 | 初始直接可见 | 关键交互 |
| --- | --- | --- |
| 顶栏 | 设备连接、ECU CAN（波特率读回）、DBC 来源/文件、Live/录制/停止/保存、时间状态 | 不显示固定的演示文本或大量无用小字；连接异常直观提示 |
| 左侧信号树 | 已加载 DBC 消息/信号搜索、中文别名/单位；多选/加入画面；已存录波和参考分类 | **多选信号同屏**，不需要每次重新开配置对话框 |
| 中央多轨工作区 | **统一横向时间轴**、同单位可叠加、不同单位分轨；每轨名称/单位/颜色、开关、缩放 | 共享纵向光标、区间选择、平移/缩放/适应、实际/参考/容差带/差异与缺口 |
| 事件轨 | 真实 `Start/Stop/Trigger/TEST KEY/IGN/CAN` 事件及来源类别 | 区分“命令发出”和“硬件/ECU 实际响应”，不假装同步已认证 |
| 右侧详情 | 光标具体数值、原始 CAN ID/DLC/DATA、DBC 解释、捕获质量、参考/比较结果 | 点选轨道/真实采样点可跳原始帧与对应 TEST Occurrence |
| 底部 | 简洁采集进度、被录制目标数量、样本数/源 gap、未保存警告/错误 | 不隐藏证据缺失，也不把“捕获成功”画作 ECU PASS |

**显示约束**：
- Bool/Enum 展示阶梯/状态带，超过消息最大新鲜度后断开/未知；不要画线性斜坡制造不可能的状态。
- 连续 CAN reported numerical values 可用散点+折线表达观察趋势，标明“非外部探头实测连续电气波形”。
- 不同物理单位默认不同 Y 轴轨道，不能共用无单位的值轴；同单位多条实际/参考可透明叠加。
- 实时/历史、多轨/单轨切换不改变原始数据/判定事实。绘图可降采样/保留峰谷，记录不能因为 GUI 卡顿丢帧而静默仍 PASS。
- 任意信号无新帧、流变化、USB 断开时要显示“来源未知/停止更新”，不能无限延长最后数值。
- 多信号共享时间范围**只用于显示/分析坐标**，绝不自动生成“同时采样”的虚构新点。

## 3. 最小共享后端设计：source-neutral，而非第二套 CAN/DBC

当前有用的技术资产：`HardwareCanReceiveBridge → CanFrameHub → DbcLiveSignalDecoder/DecodedSignalPublisher → SignalObservation`、`WaveCaptureService`、`WaveEvidenceStore`、`WaveReferenceRepository/Catalog`、`WaveChartAnalysis`、`RealRunCoordinator` 与 `RECORD`。

### 3.1 解耦独立 DBC 的 `WaveSignalSource`

现有 `LiveDbcSignalService` 包含两件事情：
- 有价值的 CAN→已固定 DBC 信号的共享真实解码；
- 被项目/TEST 绑定所限制的 `BindAsync`/`OpenAsync`。

建议把它分为**通用已固定 DBC 信号源适配**和**工程 TEST 的资源解析器**：
- 统一输入一个明确的 `PinnedDbcSource`（来源=StandaloneFile/CurrentProject/Archive; 完整 bytes hash / AssetRev / signal identity），不强迫传 ProjectWorkspace；
- 对独立文件生成不可重用冲突的临时资源身份，并用真实 bytes 固定引用，保存时记录冻结快照或受控依赖；
- 工程 TEST 仍从 PreparedPlan 的 `TargetMapping` 解析为相同的 `PinnedSignalRef`；通过**同一个** `DecodedSignalPublisher/DbcLiveSignalDecoder`/CanFrameHub 解码，不允许两页自行写一套 DBC 算法；
- 多信号订阅/计数从**同一个 CAN session epoch** 开始；多条信号不需要重复开 USB 或占用多个写入授权；
- 外部 DBC 改动或工程自动保存时不修改在录制的 decoder/SignalRef；明确冻结/提示失效/下次重绑。

**接口草案（名称非冻结 API）**：
`
WaveSourceSelection:
  ResourceOrigin: StandaloneFile | CurrentProjectPin | ArchivedCapture
  DbcBytesSha256: sha of exact external/project bytes
  DbcAssetRef?: stable id, revision, sha when saved as project resource
  BusRole: DutCan
  CanBaudConfigured/Readback
  StationEpoch/DeviceIdentity
  SelectedSignals[]: { CanId, IsExtended, SignalName, Unit, Semantic, MaxAge }
`

Source-selection/decoder/session 项与工程 TEST 只是**两种适配入口**；不为 Wave 新建独立硬件连接驱动，也不创建第二套 DBC 编辑器或第二个永久 Record DB。

### 3.2 多轨捕获组 `WaveCaptureSet`

`
CaptureSet:
  CaptureSetId
  SourceContext and SessionEpoch
  Origin: HostUtc + ClockSource + MonotonicTickFrequency/ClockQuality
  ArmedAt / ActualSubscribeReadyAt / TriggerEvents[]
  Window: requestedBefore, requestedAfter, actualStart, actualEnd, EndReason
  Tracks[]:
    SignalRef, Semantic, RawFrameSourceRefs[], OriginalActualSamples[],
    DataQuality: sampleCount, first/lastSeen, maxAge, intervalDistribution,
                 SourceLossDelta, HubLossDelta, SinkLossDelta, BufferLoss,
                 isValidForJudgement, reasons[]
  CrossTrack:
    IndependentSignalClocks=true (unless independently qualified otherwise)
    SharedHostObservationClockDomain=true if same process epoch
  ArtifactIds / Sha256 / OptionalTestRunLink
`

- 使用一次逻辑 CaptureSet 协调多条 SignalRef，但各条信号保留**原始异步的时间和样本数**；UI 同轴叠加不能按“最近值”生成一份假的同步实测波形。
- 控制采集时按**所有目标订阅者真正建立后**出具 `Ready`；记录源高水位及开始窗口。已有 `WaveCaptureService.StartAsync` 的 Origin 早于异步枚举真正进入 `ObserveAsync`；必须补实际就绪事件。
- 若其中一路信号没有新帧：它的轨道质量为 NoFrame/Stale；其它轨道仍可以观察。正式多信号判定如果需要该轨则整体 INCONCLUSIVE，不必把整个工具 UI 禁止显示其它信号。
- 真实 CAN 总线已有广播和 subscriber drop，`CaptureSet` 必须采集**本窗口差分**而非把永久累计量直接判为当前 session 失败；Epoch 改变不能拼接。
- Stop/Timeout/Disconnection/Cancelled/Overflow 为不同 `EndReason`，保留已有可归档的事实，**不得把不完整采集作为正式 PASS 的充分证据**。
- 每一条原始 SignalObservation 保留 SourceEvidence，正式 TEST 逐轨映射到本 Run 的原始 CAN RX 事实，记录 Pinned DBC decode proof。
- 多轨不要求 R1 首版执行跨轨复杂状态机；P0 **必须可多轨看/录/保存**，跨轨 `SignalA→SignalB` 时间关系作为额外正式判定可随后受控增加，不能以“两个单信号均 PASS”假装跨轨时序通过。

### 3.3 存储归属

工具自由录制的真实事实是 `ToolCaptureId`，不强制拥有生产 `RunId`。数据保存应可重开/导出，带完整来源和 SHA；个人本地工具资源与项目目录是不同作用域（具体默认物理路径/清理政策待技术定点），若用户显式将 capture/reference 纳入当前工程，则由 PROJECT 执行**复制与固定依赖**，不可仅在外部 DBC 路径保存一个脆弱快捷方式。

正式 TEST 由 `RealRunCoordinator` 负责执行、开始/结束 StepOccurrence、报告与作用域隔离；它创建的 CaptureSet 加注本次 `RunId/CaseAttempt/Start+Stop+CompareOccurrence`，通过唯一正式 RECORD 中 `VerifiedWaveComparison`（名称待审） 与 raw CAN SHA 证明才能参与生产 PASS。**工具级录波和 TEST wave 捕获共享核心服务，但不会隐式相互变成正式结论**。

## 4. Accuracy Gate：五个必须证明的事实，不能只调“相似度”

**(1) 源字节**：真实 CAN ID、标准/扩展、DLC/Data/BusRole、USB SessionEpoch/RawEvidenceRef；总线完整性在没有 ECU sequence/counter 保证时仍不可证明“ECU 一帧不漏”。

**(2) 解码数值**：`DbcAssetSha` + endianness/signedness/factor/offset/mux/invalid/quality bits + Unit；原始报文按相同已固定 DBC 离线重算应等于录波的 original sample；浮点显示位数不改事实。

**(3) 采样/时间**：`HostArrivalOnly` 并非 ECU 内部硬件事件时刻；每个目标信号实际发送周期/样本间隔/gap、USB/Hub/Subscriber掉帧、UTC跳变、同 session monotonic frequency 必须记录。用户批准的时限只有在测量系统实际精度与适用性支持时才可正式判定。把“CAN 500 kbit/s”当成“波形 500kSamples/s”是错误的。

**(4) 可观察判定**：状态机/Enum用前态→目标态、观察窗口/旧值失效期限；数值用采到的值域/经过检验的有效覆盖时间；参考比较先验证适用型号/工况/DBC与冻结版本，不许用默认最大 shift 让超时信号被挪齐。多轨关系没有可信一致时钟时不能宣称物理同步时间。

**(5) 已归档的正式证据**：`CaptureSetId/SignalRefs/Actual/Ref/AlgorithmPin/Run/Occurrence/RawCANArtifactSha`，重要缺测/冲突明确 `INCONCLUSIVE/ERROR`。`WaveComparisonPendingArchive` 与 `RunStepFact.Validate` 的现有冲突要先有精准红灯集成测试，再分类型修复，不能删掉 RECORD 校验。

### 推荐的判定输出

| 结果 | 原则 |
| --- | --- |
| `PASS` | 评价窗口完整，必需真实信号质量可证，正式规则所有必需指标满足，归档证据一致 |
| `FAIL` | 可信的实际观测证明违反具体产品条件，记录失败时窗/原始帧与原因 |
| `INCONCLUSIVE` | 数据/窗口/时间精度不足，或关键轨道无新帧/不可检查；不冒充 ECU 产品 FAIL |
| `ERROR` | 配置/DBC/参考/存储/归档不一致或算法无效，正式 PASS 禁止 |

算法返回四态 ≠ 出厂最终状态。生产还包括前置真实烧录、整个已启用 TEST 用例树与相关记录；这一层不由 WAVE 执行。

## 5. 正式 TEST UI 语义必须简单，避免发明生产配方

在已有 TEST Step Inspector（或经审查的多个现有 `WaveStart/WaveStop/WaveCompare` 节点）直接设置：
- **信号**：从工程当前生效 DBC 查找/中文别名；判单信号或多个有明确关系的信号；
- **任务**：观察状态变化、数值区间、与工程内已批准参考比较；
- **动作与窗口**：绑定当前用例的真实 StepId/事件语义，定义预采、响应时限、保持时长/退出；
- **阈值/数据质量**：业务规格、最大 gap/最小有效观测/时间证据要求，不用全局一律 5%；
- **结果提取**：将真实响应时间（仅当前可测主机观察意义）、最大/最小、目标状态/质量及异常起止写入 TEST 关键记录/报告；不要求每个用例再编 CAN 日志/保存报告步骤。
  
“方便插入”可提供一个**可见、可展开、可编辑**的波形步骤组模板；插入后在测试步骤列表里可见，右侧 Inspector 可调整。不要为了缩短 UI 建立独立的影子 TEST DSL/Runner。自动保存只是编辑草稿的持久化，不使非法/未批准参考自动具备执行权。

生产不添加第二层`Wave ProductionConfig`。波形规则存在于当前工程 TEST 用例及其已固定资源引用；生产只运行已启用用例，不受开发勾选的临时范围、工具临时 DBC 和独立录波影响。

## 6. 从现有 R1 到可实测实现的最小切片

| 次序 | 源代码范围 / 负责人 | 明确改动 | 真实准出证明 |
| --- | --- | --- | --- |
| W12-A | `MainWindowViewModel.OpenWaveWorkbench` + `WaveWorkbenchViewModel` / UI | 去掉 Current Project 入口门槛，持有独立资源上下文；多轨选取/显示/Live/开始停止记录 | 无工程能开，独立DBC两条真实信号同屏、有错能关闭恢复 |
| W12-B | `LiveDbcSignalService / WaveCaptureService / WaveEvidenceCodec`；DEVICE/DBC | 抽取项目中立的 PinnedDbcSource→shared CAN signal reader、捕获组、多路实际监听及质量；绝不复制 CAN/DBC 协议 | 多条真实 SignalRef 的报文/值/时间/丢失在同一 epoch 可复核 |
| W12-C | `WaveReferenceWorkspaceService / WaveReferenceCatalog`；PROJECT | 工具本地录波/参考资源，与“纳入工程”明确转换，源DBC与参考内容冻结、无 replay假批准 | 不修改工程的独立录波、显式导入后可供 TEST；错版拒绝 |
| W12-D | `WaveComparator/WaveTimeQuality`；WAVE | 语义分型、gap/窗口/trigger/shift/4-state；多轨显示 ≠ 跨轨时限承诺 | 回放对抗测试红绿+真实 ECU 正反/缺帧 |
| W12-E | `ConfiguredWavePort/RealRunCoordinator`；TEST | 保留唯一 TEST，独立 Capture/Run/Occurrence 生命周期，旧 captureKey 作用域/循环/取消对齐 | 波形能在 TEST 真运行、循环两次不串证据 |
| W12-F | `RealTestRecordSession/RunFact/Finalizer/RunReportExporter`；RECORD | Wave 类独立严格 evidence、原始 CAN 关联、简洁报告关键项 | 正式 PASS/FAIL/INCONCLUSIVE/ERROR 归档、失败不盖历史 |
| W12-G | WPF UX/自动保存/主窗关闭，联合回归 | 高 DPI、多轨滚动/光标、工具共享 CAN/安全读写、错误恢复 | Windows Debug+Release/相关测试+两窗口真实 USB 测试 |

上述为**候审实施次序**。特别提醒：W12-A 用户产品目标是**无工程+多信号同时可用**，不能先把单信号功能包装成“WAVE 工具已经完成”；W12-D/E 单信号先闭环可以作为内部算法/测试节点。

## 7. 专门的反误判/实测矩阵（请先验证，不代填）

| Case | 场景 | 正确结果 |
| --- | --- | --- |
| WT2-01 | 主程序无工程，打开波形工具，选独立 DBC，多选至少 2 个真实 ECU 信号 | 可同时 Live/记录，未造工程/TestProgram/ScanRecipe |
| WT2-02 | 工具使用独立DBC，同时另有工程A已打开 | 工具不会偷偷替换工程的唯一有效 DBC；独立来源可见 |
| WT2-03 | 同个真实时间窗里状态 enum 0→2 与数值电流同时变化 | 分轨显示、统一 X 游标，但 enum 不插值状态1；没有伪造同一瞬间两个采样 |
| WT2-04 | 目标状态10Hz，另一信号50Hz，先断一条 | 失联轨明确为 NoFrame/Stale；另一轨仍可看，联合正式判定不能 PASS |
| WT2-05 | Windows UI 高负载、拖拽、缩放、图形更新慢 | 不阻塞 CAN 源；录制质量有 drop/overflow 证据；仅绘图可丢帧 |
| WT2-06 | 捕获开始后延迟到 sink 真正 ready，动作紧跟 Start 点击 | 不能错过动作前合法 trigger；没证据就不能认“已就绪” |
| WT2-07 | USB断线/重连、电源阶段切换、项目切换 | 原 CaptureSet 结束/质量不足，新 epoch 不拼接，工具不抢写 |
| WT2-08 | 原DBC被删除或修改后重开离线录波 | 仍可用冻结DBC/来源元数据解释，或明确拒绝；不套当前最新DBC |
| WT2-09 | 从独立工具提交录波纳入工程，信号SHA不匹配当前唯一DBC | 应明确拒绝或要求工程变更/人工审查；不能静默重绑定 |
| WT2-10 | 纯 Replay capture试图发布工程正式真实参考 | 开发比较允许、生产参考审批不通过 |
| WT2-11 | enum响应实际比参考晚40ms，旧profile允许50ms shift，但规格只有20ms | 时间规则不能自动把延迟挪齐变PASS |
| WT2-12 | 数值采样间存在物理尖峰，但总线ECU不上报 | 系统只可声明“已上报采样值”结果，不证明无物理尖峰 |
| WT2-13 | 当前TEST运行时用户打开工具并另选波形 | 只读工具行为不改PreparedPlan波形SignalRef/阈值/触发、不抢写 |
| WT2-14 | CaptureStart/Stop/Compare跨两次Loop同名captureKey | 每次CaptureSet与StepOccurrence独立；raw CAN附件不串 |
| WT2-15 | WaveCompare pass but step evidence kind未被RECORD验证接受 | 不发正式PASS，集成红灯；修复后证据一致才通过 |
| WT2-16 | 真 TEST运行后打开简明单台报告 | 仅关键波形测量/FAIL原因进入正文；完整轨/Raw CAN可追溯打开 |
| WT2-17 | 真实生产全部Enabled，其中一个WAVE用例INCONCLUSIVE | 不宣告“出厂全部PASS”，失败处理依冻结TEST/生产总流程的批准策略 |
| WT2-18 | 工具无工程正常录波后退出主程序并重开 | 记录保留、SHA一致，独立工具本地库/路径能恢复；不留孤立进程 |

**时间精度专项门槛**：目前只能确认 `HostArrivalOnly`；必须用真实 ECU 加可信独立测时证据才能宣称物理响应延时的确定上界。CAN 波特率与 ECU 更新周期不能混称“波形采样频率”。没有相应设备/外部测试仪器时，本版本只能申请`有条件的主机观察级功能时序判定`，不宣称高频物理示波器能力。

## 8. 报告/证据端必须按现行 PM 四层职责给出输出

1. **完整 CAN 底本**：真实接收/发送 CAN 帧、DBC解码来源及 WAVE 全样本，当前检测窗口持续记录，报文不全可见。
2. **TEST 关键测量与日志**：从经批准 StepOccurrence 提取状态/响应观测时间/数值极值/时间质量/阈值/FAIL片段。
3. **简洁单台报告**：一台 ECU 的某次 Inspection 中显示该项 WAVE 的 PASS/FAIL/INCONCLUSIVE/ERROR、为什么，参数/阈值摘要，证据链接；不把全部CAN帧粘正文。
4. **批量报告**：作为 RECORD/SCAN 整体职责按已有检测事实聚合，WAVE 无需另建波形批次统计引擎。

开发工具的独立录波默认不是生产 Inspection，也不因此产生扫码或批次统计数字。真实 TEST 的 WaveMeasure/Comparison 是当前用例的结构化证据，正式报告不能通过读取某个“最近一次工具曲线”拼结果。

## 9. 本专项需要向 R1/PM 明确反馈的最小决策点

无需重复提问已确认的“无工程工具、多轨、共享DBC/源、生产自动运行树”。
1. **必须共同审查**：DBC/DEVICE 如何抽出不依赖 ProjectWorkspace 的 signal source adapter，并保留真正的 Shared CanHub/原始 evidence；单独纯 Wave 研究分支不单方面改共享设备合同。
2. **必须共同审查**：RECORD 对正式 WaveComparison evidence 采用有限强类型验证、入库/归档阶段状态与报告回读，不能“临时关闭 Validate”。
3. **须以实际 ECU 定点**：哪些状态/数值信号能作首批可判示例，具体每信号阈值、报告周期、最大gap与时间不确定度从实测+产品规格来。
4. **可先以合理默认建议、无需用户逐字段讨论**：独立工具本地录波与工程导入操作、浅色多轨 UI、两光标/可见参考建立入口。它们是 UI/PROJECT 候审交互，先做成熟默认并交给用户实际操作微调。

**本文件只建议单一复用后端、两类用户操作入口、一套正式证据事实链，不引入第二 TEST、第二 Production WaveConfig、第二 CAN 驱动或 Recipe 层级。**

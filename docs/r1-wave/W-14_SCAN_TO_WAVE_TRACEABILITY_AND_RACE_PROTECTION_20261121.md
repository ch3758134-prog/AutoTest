# W-14｜扫码 × WAVE：真实串口输入、跨 DUT 波形归属与产线误判防护

> **2026-11-21 / Issue #71；专责：WAVE 的身份、时间、采集与判定证据交界。** 项目管理最新 `Project_Management/current`，SCAN 专项 `S-11/S-13/S-14`，R1`@5f7a2cf42c1a30434431af18e2fb26741075ad2f`。状态 `CROSS_SPECIALIST_CONTRACT_PROPOSED / WINDOWS_NOT_RUN / SERIAL_SCANNER_FIELD_NOT_RUN / ECU_FIELD_NOT_RUN`。
>
> **用户额外真实确认：当前扫码枪是 USB 串口/虚拟 COM，而非键盘模拟枪。** 一些旧管理文件仍把 HID/COM 写成“未定”；本专项尊重用户最新更明确输入，现场仍需核对 COM 实际 USB VID/PID/接口实例、速率与帧尾，以确定具体驱动参数。条码格式本身不是已冻结 UT1，只有型号+稳定唯一编号的业务要求已确认。

## 1. 为什么 WAVE 也必须研究扫码？重点是“判了哪一台”，不是“替 SCAN 解码”

扫描枪负责给生产工位一台 DUT 的**业务标签身份**；WAVE 负责从主控获得**物理通信源上的事实**。单靠扫到了 `SN=A`，不能证明 CAN 帧一定来自 A；必须通过当前站点唯一 DUT 的**运行上下文、设备会话、测试触发、CAN 更新事实和可用时的 ECU DID** 建立合理追溯。没有 ECU DID/物理电气身份验证时，报告只能说“本次工位接受编号 A，对当前物理连接观测到的 ECU 数据”，不能声称已用加密手段验证条码与 PCB 的绝对一致。

SCANNER 不能调用波形比较器，更不能自建 TEST、Flash、记录数据库。WAVE 不能从条码字符推断需要哪些 DBC、用例、固件和参考；当前型号工程的有效资源是唯一合法选择来源。两者只在 `ProductionInspectionContext` 与 `RECORD` 交汇。

## 2. 最新生产动作顺序，必须严格保持

`
WAIT_SCAN
 → USB COM 扫码完整记录（真实端口 + 真实字节/帧）
 → Barcode ModelCode/Serial 经 SCAN 校验、找到唯一已安装 ECU 工程
    same Model：不弹窗；known different Model：确认项目切换
    unknown/ambiguous：拒绝，不创建正式运行
 → InspectionAccepted: 固定 ScanEventId / InspectionId / ModelCode / DutSerial
 → 请求夹具压紧；真实到位事实另由 DEVICE/HW 验证
 → WAIT_PHYSICAL_START（未按实体 START 不烧录/不跑 WAVE TEST）
 → 真实新 START 事件 + 工位/工程资源预检/冻结
 → 每次本次真实 FlashAttempt；失败不进入 TEST/WAVE 正式判定
 → Flash verified → 真实 TEST 执行全部 Enabled 用例
      进入有 WAVE 的测试步骤：pre-arm 真实订阅
      独立 KEY/IGN/CAN stimulus event
      在指定时间窗记录当前 ECU 新产生的目标 CAN 帧
      Wave judgement 四态 + 原始 CAN/Reference/Algorithm 证据
 → 归档 Inspection→CaseRuns→StepOccurrences→WaveCaptures/Report
 → 收尾与结果保持；仅在下一合法扫码后更换当前身份
`

**不能把扫码时间当波形 Trigger，不能把 Flash 完成时间当 EPB Apply 开始时间。** CAN 在 Flash、APP 重启和 TEST 切换阶段出现的新旧状态/ID 都需要分会话处理，只有在本次明确订阅和动作/窗口下采得的真实新帧才进入相应规则。

## 3. 运行身份模型（建议最小必要关系，不增加新的可见 Recipe）

| 身份/事实 | 由谁产生 | 作用 | 绝不可混淆 |
| --- | --- | --- | --- |
| `ScanEventId` | SCAN | 单次真实 COM 扫描输入事件，包含 RawText/DeviceInstance/FrameQuality/Time | 不是 DUT UniqueCount，也不是 CaseRunId |
| `ModelCode + DutSerial` | SCAN 解析并由工程路由确认 | 实体业务身份；同号复检可重复使用 | 不是从当前 CAN source 解出的 ECU DID |
| `InspectionId` | Production/RECORD | 每次正式接受的完整检测尝试身份；同号重扫产生**新** Inspection，但不增加实体数 | 不是 ScanEventId，也不是 TEST 单用例 RunId |
| `ProjectSnapshot/AssetPins` | PROJECT/Production | 已批准型号工程的唯一当前 DBC/TEST/Flash/Reference/Rules SHA | 独立工具“当前临时 DBC/参考”不得覆盖 |
| `FlashAttemptId` | FLASH/RECORD | 每次 Inspection 的独立真实烧录尝试/失败 | 不等于 Wave Capture，也不当作 TEST Step |
| `CaseAttemptId + RunId` | TEST/RECORD | 正式用例一次执行；同一次检测可有多个 Case Runs | 与内存 ProductionSession 预造 RunId 不应不经映射就声称相同 |
| `StepOccurrenceId + TriggerEventId` | TEST | WAVE 开始/动作/结束/比较的精确当前步骤实例与可观察动作锚点 | 不用同名 `captureKey` 吞掉循环差异 |
| `CaptureSessionId / StationEpoch / SourceFrameIds` | WAVE/DEVICE | 本次真实 CAN/DBC 样本及原始附件归属 | 不属于上一台 ECU 或上一回 USB Session |
| `WaveRulePin + ReferencePin + Verdict` | WAVE/TEST/RECORD | 可复算的四态判定、失败区间和关键测量 | 不靠扫码规则或 UI 日志文字自动发布生产 PASS |

**唯一且不可更改的正式上下文**（结构候审，不是现成 C#）：
`
AcceptedInspection:
  ScanEventId, RawScan, ModelCode, DutSerial
  InspectionId, ProjectId+ManifestSha
  FrozenEnabledCaseTreeSha, DbcSha, FlashJobSha
  StationDeviceEpoch, AcceptedAt
AfterFlash:
  FlashAttemptId, FlashVerifiedAt, ECUSoftwareEvidenceIfAvailable
PerCaseRun:
  CaseAttemptId, RealRunId, CurrentTestSessionEpoch
PerWaveCapture:
  CaptureSessionId, WaveStartOccurrence, ActionOccurrence(s),
  StopOccurrence, CompareOccurrence, SignalPins[],
  SourceFrameEvidenceIds[], Clock/Gap/TimeQuality, ActualSha
Judgement:
  RuleVersion+Sha, ReferenceId/Revision/Sha, Verdict,
  RawCanArtifactSha, ResultArtifactSha, RecordStatus
`

在用户签约的单工位单 DUT 条件下可用这个结构保证软件归属链；**若用户现场可以在未扫码的情况下更换 ECU/松夹具而程序无感知，则软件不可能物理证明身份不变**。应由真实夹具/工位事件、可用 ECU DID 和操作规程共同验证，而不是代码凭“上一台还连接”判通过。

## 4. USB 虚拟 COM 扫码枪真实接入所需的准确性研究

R1`HOST/src/AutoTest.Specialists/Scan/SerialBarcodeCapture.cs` 只包含 `IVerifiedBarcodeLineSource`，本身**没有** `System.IO.Ports.SerialPort` 的真实打开/枚举/设备实例确认代码。`ReadAsync` 从 LineSource 返回字符串后去尾 `\\r\\n`，是被动消费，不足以证明 115200/9600 波特率、包边界、断连恢复或 COM 设备真身份。

WAVE 只要求 SCAN 提供经过验证的**完整** `AcceptedScanEvent`，串口驱动具体实现由 SCAN 管。建议 SCAN 必须验证：

- Windows 真实扫描枪 USB-PnP instance ID、COM 号、USB 拔插重枚举，不能仅按旧 `COM3` 字符串误连接一个无关设备；在选择端口时展示设备可辨身份和连接状态。
- 波特率/数据位/奇偶/停止位及真实扫码枪结束帧：`CR`、`LF`、`CRLF`、固定长度或厂商二进制协议，应按真机选；`SerialPort.DataReceived`、BaseStream read 可能一次给半包/多个包，必须**累积缓冲再分帧**，不能把一次串口事件当一条完整扫码。
- 长度上限、乱码、超时半包、控制字符、编码（ASCII/UTF-8/厂商格式）、重复终止符、噪声和重复上报；不能任意 Trim 去前导 0/改变大小写/SN 语义。
- 多次快速扫描、运行中扫描：本次 `InspectionId` 在 SCAN/Production 受理后冻结，忙时输入**拒绝不排队、不覆写当前实体**；是否把拒绝输入保存审计元数据由 RECORD/SCAN 签订，WAVE 不记录成新的 Capture。
- 无扫码时工程师依然能单独 TEST/独立波形工具；不要把 SerialScannerDisconnected 当所有模式的执行阻断。
- 可以在设备设置中“测试扫一次/显示真实帧/设备身份/CRC与解析状态”，但不要求每台正常生产重新选择 COM 或重复弹确认。

Windows 官方串口 DataReceived 文档：https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.datareceived ；
串口流读取和解码应按此异步/部分帧性质处理。上述是产品和接口验收要求，实际 scanner 物理参数仍待用户设备/Windows 真机日志核定。

## 5. 真正能防止“上一台 ECU 波形判给下一台”的 Gate

### Gate 0：入站身份
- 只在真实 WAIT_SCAN 且工位具备接收条件时接受完整且唯一的扫描事件；
- 型号决定**整个工程当前冻结资源**，不能凭上一个缓存的 CurrentDbc/LastReference/SelectedTest 覆盖；
- 同号复测可以接受为新的 Inspection，历史不覆盖、不重复计为新 ECU；新 Inspection 不能继承旧 Capture Evidence。

### Gate 1：生产 Flash/ECU 状态新鲜
- TEST/WAVE 正式 capture 必须在**本次已验证完成的 FlashAttempt 后**启动；若 Flash 失败，TEST 不启动，原始阶段证据保留；
- Flash/APP 复位可能改变 ECU 输出及 CAN 状态。波形`PreArm` 需要确切的“订阅生效 + 当前 ECU/StationEpoch + 新鲜目标帧”事实；清空逻辑 `Latest` 缓存只是必要技术步骤之一，还需新帧事件/水位证明。
- 仅有同 USB StationEpoch**仍不一定证明 PCB 未换**；可用且真实支持时额外核对 ECU DID/诊断身份与型号，否则保留“标签绑定来自工位操作”的证明等级说明，不猜 DID。

### Gate 2：每个 TEST 实例独立触发/采集
- TEST WaveStart 已明确登记本 Run/CaseAttempt/StartOccurrence/CaptureSession/StationEpoch/SourceSequenceHighWatermark；
- KEY/IGN/CAN 的真实发送属于动作事件；`WaveStart` 不可自动被解释为物理 KEY 已闭合。
- Stop/Cmp 只消费和当前 StepOccurrence/调用路径/循环轮次相关的 CaptureId，不用全局同名 Key；可以重复执行同名流程，但实际数据绝不能互串；
- 对当前需要判断的目标帧要求 `FirstFrameSeq > PreArmHighWatermark`（若设备能提供该序列；否则用当前会话真实新分发事件ID+窗口时钟证据），不能用上一台 `Latest` 读取完成判定。

### Gate 3：RECORD 归档和结果
- 所有 WAVE actual sample SourceEvidenceRef 应能落在本次 Run 的 RawCAN RX 事件范围内；Reference ID/Rev/SHA、DBC SHA、Profile/Algorithm Version 不可与冻结计划不同；
- `RealTestRecordSession`/`RunStepFact.Validate` 的 WaveComparisonPendingArchive 与 DirectDbcSignal 不匹配是现有已证静态阻断，需 TEST/RECORD 一起修独立严格的 Wave evidence Type 与归档最终证明；
- 当来源/归属缺失时无论算法 RMSE 或 similarity 多高都只能 `ERROR/INCONCLUSIVE`；绝不发布 PASS，也不要将“没有记录到显式 FAIL”记为产品合格。

### Gate 4：后续扫码、复检和工具并行
- 扫另一台或同号复检前保证上一次实际 Capture 关闭/归档完毕；若断电造成悬挂 Artifact，启动后归类“不完整旧 Inspection”，不能自动变成新实体证据；
- 工程师可以开启独立 WAVE 工具并发**只读观察**，但它的临时 Reference、DBC 和收集到的 Live 值不能偷偷注入 TEST 的已冻结信号映射或 Verdict。
- 运行中再次扫码应拒绝而非重写当前 DutIdentity、改项目、切 DBC 或把本次样本挂到新 Serial。

## 6. 扫码与波形算法的具体错误案例

| Case | 实际输入/状态 | 正确可解释结果 |
| --- | --- | --- |
| SW-01 | USB COM 半包 “EPBA-0000” 然后 “0123\\r\\n” | 只生成**一次完整** ScanEvent；未完整不得开始 Inspection |
| SW-02 | COM 一次接收 “A\\r\\nB\\r\\n” | 两个输入事件依序分帧，但站点第一台被受理后第二台因忙被拒绝/不排队 |
| SW-03 | 用户已确认型号 A，快速扫到型号 B 且上一台 TEST 正执行 | 拒绝 B，不切工程/Reference/DTD，不覆写 A 的 WAVE Capture |
| SW-04 | 同号复检，上一次 WaveCompare PASS 保存在 `_completed[captureKey]` | 新 Inspection/CaptureSession，必须使用本次实际新 CAN；绝不可继承上一轮结果 |
| SW-05 | 正式生产 STEP 开始前 ECU 未启动/无目标新帧，但 `Latest` 仍是上一台的目标值 | NoFrame/Stale/INCONCLUSIVE，不能 PASS |
| SW-06 | 扫码合法后夹具压紧请求已发送，未获得实体 START 新边沿 | 不触发 Flash、TEST/WAVE 正式采集；可保留独立工具观察 |
| SW-07 | Flash 本次失败；旧 Reference/旧 CAN 看上去仍满足状态 | 本次不存在正式 Wave 判定，TEST 记 NOT_EXECUTED；Inspection 保留 Flash FAIL/UNKNOWN |
| SW-08 | Flash 完成 APP Reset/USB epoch 变化，一次 WaveStart 跨两段 | Quality INCONCLUSIVE/ERROR，不拼接成连续趋势 |
| SW-09 | 型号和当前工程 DBC SHA 不符，或已发布 Reference 是前次型号 | 预检/比较 ERROR，不能使用相似度取巧 |
| SW-10 | 实际 CAN 最后一次目标状态是旧 ECU ，扫描完成立即启动未 prearm | 不可将旧值计成目标新鲜样本，必须明确 PreArm/SignalUpdate 证明 |
| SW-11 | 多 CASE 同 RunGroup，各自 RunId 不同 | 都挂到同一个 Inspection，但 StepOccurrence/RawCAN 时间范围精确归属 |
| SW-12 | 工具在独立DBC下采集同 CAN ID signal，生产正式 TEST 用项目DBC | 工具结果仅诊断，不会修改 Production PreparedPlan 或正式 Wave 判据 |
| SW-13 | 同一条码重复扫描 3 次、其中一次触发正式复检 | ScanEvent 3个可审；Inspection/FlashAttempt 仅对被受理的检测建立；唯一 ECU 数仍按真实唯一身份计1 |
| SW-14 | 检测被暂停/记录磁盘已满，用户换下一台 | 不完整证据标 Unknown/ERROR，不能从旧缓存发正式 PASS，不得冒充已安全换台 |
| SW-15 | 用户要求从条码型号直接推断 ECU 内部固件版本 | 拒绝假推断；型号工程选择固件，实际 DUT 版本需真实 UDS/DID 证据（若支持） |
| SW-16 | CRC 合格，但操作员给错误 PCB 贴上了别台标签 | CRC 只能保证标签文本校验，不能证明物理 PCB 身份；保留身份验证能力等级并建议独立 DID/工位核对 |

## 7. 从最新 R1 源码得出的跨专业实际缺口

- `Scan/SerialBarcodeCapture.cs`：只有`IVerifiedBarcodeLineSource`，`ReadAsync` 消费一行字符串并去 `CRLF`；没有真实 Windows COM 打开、PnP 实例绑定、半包处理端到端实现（用户已说明实体 USB 串口枪，优先真实 COM，不再默认键盘）。
- `Desktop/MainWindow.ScannerEntry_*`：以焦点输入式键盘入口为主，不等于已适配真实 COM；请 SCAN 按用户实际设备处理。
- `Desktop/Projects/ProjectScanMatcher.cs`：当前只对当前工程 ScanRecipe 评估；不等于跨工程正式 ModelCode 索引/站点身份受理。
- `Application/Production/ProductionSession.cs`：当前内存 `ProductionJobSnapshot(RunId.New...)`；正式 TEST 多 CaseRunId 不可假设相等，无结构化 InspectionId/CaseAttempt 关联实测。
- `Application/Production/ProductionWorkflow.cs`：已有 Flash→TEST 框架，但 Flash 成功时使用 `RunFactKind.MockObservation`；此源不支持正式“真实 Flash 已证完成”，RECORD 必须实际校验，不能被 Wave PASS 补救。
- `Desktop/Testing/ConfiguredWavePort.cs`：单 `captureKey` 两个字典作为全局键；`_completed` 同名阻断重复 capture，循环/重试需要每 occurrence 分离；缺 ScanEvent/Inspection 的正式运行引用。
- `Specialists/Wave/WaveComparator.cs`：即使输入样本“属于别的 ECU”且 DBC ref 名/版本一致，它只检查 Signal 与 SourceRevision，不可自己知道实体身份；**正式生产必须由 TEST/RECORD 的外围 provenance Gate 证明**，不应把识别条码的知识硬塞进 pure comparator。
- `RECORD`：WaveCompare 阶段事实入库冲突还需修，不应因该波形所属 Inspection“扫码正确”就绕过校验。

## 8. 责任和最小交接清单

**SCAN**：用户确认 USB/COM 后的真实驱动、分帧/重连/真实输入身份、条码 ModelCode+Serial 解析、忙时拒绝与跨工程唯一型号路由。具体 BarcodeCodec/串口物理参数由 SCAN；**WAVE 不实现 COM 读帧器**。

**PROJECT/Production**：受理一个 InspectionId、版本/型号/当前工程与 FlashPlan 冻结；等待真实夹具与 START；每次必须真实前置 Flash 成功才调用全部 Enabled TEST；禁止无证据 Ready。

**TEST/DEVICE**：每次 wave 订阅就绪、真实动作触发、StationEpoch、RawCAN 水位、CaseRun/StepOccurrence，防止 “上一台/上一轮” 读缓存，允许工程师工具独立运行。

**WAVE**：纯比较算法与采样质量、CapturedRunId/Context 引用、reference/DBC/algorithm pin、四态结果+解释；禁止算法层读取扫码最新输入。

**RECORD**：真实 Inspection→FlashAttempt→每 CaseRun/StepOccurrence→Capture/RawCAN/Result 的完整归档与报告重开，重复 SN 不重复新增 ECU，UNKNOWN/FAIL 历史不被覆盖。

**本专项只研究交接，不添加第二套 Scan/Production 引擎，不改 R1/Main_Control/Project_Management。**

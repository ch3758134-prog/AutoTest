# G3-03｜协议/设备适配及专业服务（全量候审 v0.2）

> SAEF G3 正式候审内容；**G4尚未实施、所有测试初始 NOT\_RUN**。

## 上游批准约束

G2 v0.7 的系统边界/12 项 ICD/状态所有权及 P0～G1 已批准内容必须保留。详见 `README_FIRST.md` 和 `docs/COMMON_DESIGN_CONTRACT.md`。

## 本工作产品适用的详细设计模块

### A1｜主控 USB：枚举、帧协议、会话

**工程：** `AutoTest.Devices`；**G2：** G2-01/02/04/05/07；ICD-01

真实主控固件源码定义 A5/5A、SEQ 小端、CRC16、CMD 和响应；参考旧 UsbSession 的请求关联与异常处理，但不直接引用旧设备层。主控 PC 驱动类型须 B0 枚举确认。

**主要调用链**：Connect → discovery → ByteLink → read loop → PING (F0) / GET\_VERSION (F1) → 可用会话；数据帧先解码，再按 SEQ=0/CMD=41 分派异步 CAN，其他匹配 pending。

**状态/异常**：Detached→Discovering→Connecting→Handshaking→Connected→Degraded/Closing→Detached；连接成功≠工位安全已就绪。 CRC/EOF/长度错误丢弃并记录；超时后结果未知；拔插使全部 pending 失败；重连世代旧应答隔离；连接不自动发送 IGN/夹具。

**设计文件 8 项：**

- `src/AutoTest.Devices/Usb/IUsbByteLink.cs` → `IUsbByteLink`；方法 `OpenAsync/ReadAsync/WriteAsync/CloseAsync`；仅负责字节流与取消。
- `src/AutoTest.Devices/Usb/UsbWireCodec.cs` → `UsbWireCodec`；方法 `EncodeRequest/TryDecodeResponse/Crc16`；无IO字节帧。
- `src/AutoTest.Devices/Usb/UsbFrameDecoder.cs` → `UsbFrameDecoder`；方法 `Feed/Reset`；处理拆帧粘帧与异常前缀。
- `src/AutoTest.Devices/Usb/UsbDiscovery.cs` → `UsbDiscovery`；方法 `ListCandidatesAsync`；不把扫码枪枚举为主控。
- `src/AutoTest.Devices/Usb/PendingRequestRegistry.cs` → `PendingRequestRegistry`；方法 `Register/TryComplete/FailGeneration`；世代、序号、命令强关联。
- `src/AutoTest.Devices/Usb/UsbSessionService.cs` → `UsbSessionService`；方法 `ConnectAsync/RequestAsync/DisconnectAsync`；单会话所有者、后台读循环。
- `src/AutoTest.Devices/Usb/MainControllerCodec.cs` → `MainControllerCodec`；方法 `EncodePing/EncodeCan/EncodeIgn/EncodeSol`；业务命令映射按源码。
- `src/AutoTest.Devices/Usb/UsbDiagnosticsProjection.cs` → `UsbDiagnosticsProjection`；方法 `ObserveState/ObserveRawFrame`；只读原始报文与异常统计。

**参考源码/资料：**

- `MainFW:Project/source/BSW/CDD/USB/CDD_USB.h`
- `MainFW:Project/source/BSW/CDD/USB/CDD_USB.c`
- `MainFW:Project/source/ASW/UsbSvc/ASW_UsbSvc.c`
- `HistoricalHOST:src/AutoTest.Integration.Usb/UsbProtocol.cs`
- `HistoricalHOST:src/AutoTest.Integration.Usb/UsbSession.cs`

**仍需通过实验关闭：** 真实USB枚举/驱动与端点、最大持续速率、固件版本识别在 B0/B1 确认。

### A2｜台架 Station：双 CAN、IGN、夹具和按键

**工程：** `AutoTest.Devices`；**G2：** G2-01/02/05/07；ICD-02/03

逻辑 DUT\_CAN / KEY\_CAN 通过经验证的 StationProfile 映射当前物理通道；主控源码注释为 CAN0=按键 CAN1=ECU，仍需现场 B2 验证。IGN、夹具均有输出副作用，ACK 不等于 ECU 端电气或夹紧到位。

**主要调用链**：上层请求→前置检查→租约→设备编码→主控 ACK→观测事实→状态 Unknown/Confirmed。按键协议帧命令0x01写mask、0x80全释放；字段由现役KeySim\_ProcessFrame源码验证。

**状态/异常**：Restricted→Ready→Executing→Acknowledged/Unknown/Observed；IGN、夹具和按键分别维护最近一次命令状态，不跨重连保留“已完成”。 未知电源/接线不发危险命令；夹具无到位反馈，机械只能手动/外部证据；KEY16 缺陷登记；单通道映射未核实前禁发；无默认电源开关能力。

**设计文件 8 项：**

- `src/AutoTest.Devices/Station/StationProfile.cs` → `StationProfile`；方法 `Validate/DescribeBindings`；逻辑角色到设备通道映射。
- `src/AutoTest.Devices/Station/CapabilityRegistry.cs` → `CapabilityRegistry`；方法 `PublishVerified/GetAvailable`；只发布已证实能力。
- `src/AutoTest.Devices/Station/StationPreflight.cs` → `StationPreflight`；方法 `CheckAsync`；供电型号/接线/权限检查。
- `src/AutoTest.Devices/Station/StationControlService.cs` → `StationControlService`；方法 `SetIgnAsync/OperateFixtureAsync`；有副作用操作入口。
- `src/AutoTest.Devices/Station/KeypadCommandCodec.cs` → `KeypadCommandCodec`；方法 `EncodeMask/EncodeReleaseAll`；0x730 + 20位mask，8字节帧。
- `src/AutoTest.Devices/Station/KeypadControlService.cs` → `KeypadControlService`；方法 `SetMaskAsync/ReleaseAllAsync`；仅经 KEY\_CAN 发送。
- `src/AutoTest.Devices/Station/OutputObservation.cs` → `OutputObservation`；方法 `ClassifyEvidence`；请求/ACK/电气/机械不同证据等级。
- `src/AutoTest.Devices/Station/FirmwareIdentityTracker.cs` → `FirmwareIdentityTracker`；方法 `ReadAndCompare/RecordIdentity`；主/按键固件版本及追踪。

**参考源码/资料：**

- `MainFW:Project/source/ASW/BusProxy/ASW_BusProxy.c`
- `MainFW:Project/source/ASW/PwrCtrl/ASW_PwrCtrl.c`
- `KeyFW:WS/EPB_EOL/Sources/APP/KeySim.c`
- `KeyFW:WS/EPB_EOL/Sources/APP/main.c`
- `HistoricalHOST:src/AutoTest.Adapters.MainController/MainControllerCanAndKeyCapabilities.cs`

**仍需通过实验关闭：** 主控 IGN 初始化高有效及当前实际 ECU 端电压需 B0/B3；夹具气路电磁阀断电态现场确定；KEY16 电阻输出需要 B4。

### A3｜共享 CAN：流、发送仲裁、时间质量

**工程：** `AutoTest.Devices`；**G2：** G2-03/05/07；ICD-02/08/12

唯一 CAN 收包解析入口，DUT 与 KEY 双路携带 BusRole；原始帧同时提供日志、DBC、UDS、波形与 Runtime 订阅。发送由租约与通道所有者仲裁，禁用多个工具私自打开 USB。

**主要调用链**：主控0x41上报→源通道映射→HostReceiveTime→Hub→异步消费者；UDS/烧录请求通过TxGate→0x40发送，普通CAN监视只读，CAN速率修改与刷写独占仲裁。

**状态/异常**：Hub: Open/Closed；Lease: Free→Granted/Waiting→Released/Expired；订阅者异常不能使读循环终止。 缓冲溢出标记 Loss，不静默删除；发送失败区分 USB ACK 与 CAN 总线已被 ECU 处理；BusRole 未映射拒绝发。

**设计文件 7 项：**

- `src/AutoTest.Devices/Can/CanFrameHub.cs` → `CanFrameHub`；方法 `Publish/Subscribe/Unsubscribe`；统一扇出、每订阅独立限额。
- `src/AutoTest.Devices/Can/CanFrameCodec.cs` → `CanFrameCodec`；方法 `FromUsbPayload/ToUsbPayload`；15字节CAN字段校验。
- `src/AutoTest.Devices/Can/CanTransmitGate.cs` → `CanTransmitGate`；方法 `AcquireAsync/Release/TransmitAsync`；发送与诊断独占会话。
- `src/AutoTest.Devices/Can/CanSubscription.cs` → `CanSubscription`；方法 `Match/ReadAsync`；按BusRole/ID/IDE过滤。
- `src/AutoTest.Devices/Can/CanLossMonitor.cs` → `CanLossMonitor`；方法 `OnDrop/ReadSnapshot`；本地缓冲丢失可见。
- `src/AutoTest.Devices/Can/CanTimeAnnotator.cs` → `CanTimeAnnotator`；方法 `StampHostArrival/AnnotateReplay`；HOST到达时刻不冒充ECU采样时间。
- `src/AutoTest.Devices/Can/CanTraceProjection.cs` → `CanTraceProjection`；方法 `Append/Query/Export`；日志只读消费，不阻塞收包。

**参考源码/资料：**

- `MainFW:Project/source/ASW/BusProxy/ASW_BusProxy.c`
- `HistoricalHOST:src/AutoTest.Communication.Can/CanSubscription.cs`
- `HistoricalHOST:src/AutoTest.Communication.Can.Usb/CanUsbPayloadCodec.cs`
- `HistoricalHOST:src/AutoTest.Communication.Can/CanBus.cs`

**仍需通过实验关闭：** CAN波特率当前固件是否支持动态修改/实际配置必须源码与现场双核实；H3性能阈值由实验数据确定。

### E1｜Honeywell USB 扫码输入与编号匹配

**工程：** `AutoTest.Specialists`；**G2：** G2-01/04/05/06；ICD-04/05

实物扫码枪 USB直连Windows PC，绝不从主控USB/CAN读取扫码。输入适配必须支持待判定的键盘模拟/虚拟串口/HID路径；采集只产生原始ScanEvent，解析与规则匹配是另两层。HOST不生成实物编号。

**主要调用链**：USB扫描→RawEvent→Normalization→RuleSet→MatchDecision→工位预检→用户确认/自动策略→RunSnapshot；运行期间新扫码只形成下次候选，不覆盖当前Run。

**状态/异常**：Idle→Capturing→Parsed→Matched/Ambiguous/NoMatch→Confirmed/Discarded；未验证的输入模式不能伪装成已自动识别。 扫码枪误在属性编辑焦点输入的防护；重复扫描不是历史冲突；编号解析冲突阻止生产启动；匹配修订入快照。

**设计文件 7 项：**

- `src/AutoTest.Specialists/Scan/BarcodeInputRouter.cs` → `BarcodeInputRouter`；方法 `AttachMode/PublishRaw`；统一输入事件源。
- `src/AutoTest.Specialists/Scan/KeyboardWedgeCapture.cs` → `KeyboardWedgeCapture`；方法 `OnKeyInput/CompleteScan`；键盘模拟模式可配置焦点安全。
- `src/AutoTest.Specialists/Scan/SerialBarcodeCapture.cs` → `SerialBarcodeCapture`；方法 `OpenAsync/ReadAsync`；仅实测为串口设备时启用。
- `src/AutoTest.Specialists/Scan/BarcodeScanEvent.cs` → `BarcodeScanEvent`；方法 `ValidateSource/WithTime`；Raw、来源、时间、质量。
- `src/AutoTest.Specialists/Scan/IdentityRuleSet.cs` → `IdentityRuleSet`；方法 `Validate/Match`；编号格式解析规则受控修订。
- `src/AutoTest.Specialists/Scan/ConfigMatchService.cs` → `ConfigMatchService`；方法 `Evaluate/RejectAmbiguous`；型号、CAN、测试与烧录资源关联。
- `src/AutoTest.Specialists/Scan/ScanDraftService.cs` → `ScanDraftService`；方法 `Capture/Confirm/Discard`；扫码草稿不得改写运行身份。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Workspace/WorkspaceModels.cs`
- `HistoricalHOST:src/AutoTest.Application/WorkspaceUseCases.cs`

**仍需通过实验关闭：** 扫码枪具体型号/USB枚举/HID键盘或COM模式尚未提供，必须借实际设备实验确定。

### E2｜ISO-TP / UDS 专业诊断与测试步骤

**工程：** `AutoTest.Specialists`；**G2：** G2-03/05/07；ICD-08/12

共享 CAN FrameHub 和发送独占租约；ISO-TP 负责分段重组/流控/定时器，UDS负责请求响应关联、NRC、会话与服务权限。独立 UDS 窗口和测试步骤均使用同一诊断服务。旧HOST QuickUdsService 仅单帧请求/有限服务，不能称完整栈。

**主要调用链**：工具/步骤→UdsClient→ISO-TP→TxLease+CanHub→ECU→响应收集→关联SID/NRC→诊断事实；FlowControl 由 ISO-TP 层处理，不让UI逐帧驱动。

**状态/异常**：Session=Disconnected/Default/Extended/Programming(按ECU可用)；Conversation=Idle/Sending/Waiting/ResponsePending/Complete/TimedOut；诊断会话不是 USB 会话。 允许的服务按风险分级；只读服务先实机，0x27、0x31、0x34/36/37等可能写入服务须独立授权；NRC0x78有界等待；同一ECU与Flash不能无序并行。

**设计文件 9 项：**

- `src/AutoTest.Specialists/Uds/IsoTpFrameCodec.cs` → `IsoTpFrameCodec`；方法 `EncodeSingle/ParseFirst/ParseConsecutive/ParseFlow`；标准帧类型和长度检查。
- `src/AutoTest.Specialists/Uds/IsoTpConversation.cs` → `IsoTpConversation`；方法 `SendPduAsync/ReceivePduAsync`；FC/BS/STmin/序号与超时。
- `src/AutoTest.Specialists/Uds/UdsEndpointProfile.cs` → `UdsEndpointProfile`；方法 `ValidateIds/ValidateAddressing`；CAN TX/RX ID及地址模式配置。
- `src/AutoTest.Specialists/Uds/UdsClient.cs` → `UdsClient`；方法 `RequestAsync/InterpretResponse`；正响应/NRC/P2\*可配置。
- `src/AutoTest.Specialists/Uds/UdsServiceCatalog.cs` → `UdsServiceCatalog`；方法 `Register/GetRiskLevel`；服务参数与权限分级。
- `src/AutoTest.Specialists/Uds/UdsSessionService.cs` → `UdsSessionService`；方法 `StartSession/TesterPresentPolicy/EndSession`；会话生命周期、独占租约。
- `src/AutoTest.Specialists/Uds/UdsTrace.cs` → `UdsTrace`；方法 `AppendRequest/AppendResponse`；原始PDU/总线帧与错误可溯源。
- `src/AutoTest.Specialists/Uds/UdsStepHandler.cs` → `UdsStepHandler`；方法 `ExecuteAsync`；供测试步骤复用而非复制协议。
- `src/AutoTest.Desktop/Tools/UdsToolView.xaml` → `UdsToolView`；方法 `Send/ShowNrc/ShowSession`；独立原始请求响应窗口。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.QuickDesktop/Uds/QuickUdsService.cs`
- `CanTool:Protocol/UdsTransport.cs`
- `CanTool:Protocol/SessionManager.cs`
- `CanTool:Protocol/IsoTpReassembler.cs`

**仍需通过实验关闭：** ECU实际服务/寻址/NRC/时序参数由真实对象测试决定。先支持 ISO-TP经典CAN normal addressing的标准子集，寻址扩展受控升级。

### E3｜波形采集、参考资产与判定

**工程：** `AutoTest.Specialists`；**G2：** G2-03/05/06；ICD-10/12

波形来自实际 CAN 电机电流信号（DBC定义），先建立原始报文和解码序列的证据；采集时基默认HOST接收时间，不虚构硬件采样。参考波形带单位、版本和标定条件；比较器插件化，旧src对齐/容差/匹配率算法为可验证候选。

**主要调用链**：CAN帧→DBC SignalRef→WaveCapture→(时间质量,样本,证据)→选参考资源→Compare→判定；手动工具和测试步骤共享同一数据模型。

**状态/异常**：Capture=Idle/Armed/Recording/Stopping/Complete/Incomplete；Comparison=NotEvaluated/Pass/Fail/Inconclusive。 丢帧/时基不明/单位不一致时禁止无证据PASS；算法输入越界、零采样、重叠不足必须Fail或Inconclusive；无法用软件ACK替代电机电流曲线。

**设计文件 9 项：**

- `src/AutoTest.Specialists/Wave/WaveCaptureService.cs` → `WaveCaptureService`；方法 `StartAsync/StopAsync/Observe`；订阅DBC信号、生成CaptureId。
- `src/AutoTest.Specialists/Wave/WaveSampleBuffer.cs` → `WaveSampleBuffer`；方法 `Append/Snapshot`；单调时间序列和容量限制。
- `src/AutoTest.Specialists/Wave/WaveTimeQuality.cs` → `WaveTimeQuality`；方法 `Describe/ValidateForJudgement`；采样间隔及丢帧质量。
- `src/AutoTest.Specialists/Wave/WaveReferenceRepository.cs` → `WaveReferenceRepository`；方法 `SaveReference/LoadReference`；参考资产版本与用途。
- `src/AutoTest.Specialists/Wave/WaveComparisonConfig.cs` → `WaveComparisonConfig`；方法 `ValidateThresholds`；容差/时间偏移/最小覆盖率。
- `src/AutoTest.Specialists/Wave/WaveComparator.cs` → `WaveComparator`；方法 `Align/Interpolate/Compare`；候选算法与可替换策略。
- `src/AutoTest.Specialists/Wave/WaveEvidenceCodec.cs` → `WaveEvidenceCodec`；方法 `Write/Read/Verify`；原始采样、配置、比较结果。
- `src/AutoTest.Specialists/Wave/WaveStepHandler.cs` → `WaveStepHandler`；方法 `ExecuteAsync`；供自动测试复用采集服务。
- `src/AutoTest.Desktop/Tools/WaveToolView.xaml` → `WaveToolView`；方法 `ShowChart/MarkReference/Compare`；独立波形工具与运行证据。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.QuickTesting/Waveform/QuickWaveformComparator.cs`
- `HistoricalHOST:src/AutoTest.QuickTesting/Waveform/QuickWaveformCaptureEngine.cs`
- `HistoricalHOST:src/AutoTest.QuickTesting/Waveform/QuickReferenceWaveformCodec.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Controls/WaveformChart.cs`

**仍需通过实验关闭：** 实测波形参考门限、触发与实时刷新策略待真实ECU采样反馈；本模块不得提前冻结最终比较算法。

### E4｜烧录配置、镜像、ProgrammingJob 与仿真

**工程：** `AutoTest.Specialists`；**G2：** G2-03/05/06/07；ICD-09/10/12

CanTool只是领域参考：配置、HEX/S19、传输会话、Seed/Key、地址与段、擦除、下载、校验、复位等对象值得认识；不复制其某ECU专用顺序、算法和常数。AutoTest烧录服务必须可以被独立工具、测试步骤或生产协同调用，真实刷写另行授权。

**主要调用链**：固件文件+Profile+目标身份→Preflight→不可变JobPlan→TxLease+UdsSession→Workflow阶段→校验/重启→持久化JobResult；Stage顺序按ECU Profile决定，不能硬编码所有ECU流程。

**状态/异常**：Job=Draft→Preflight→Ready→Running(stage)→Verify/Complete/Failed/Interrupted/RecoveryRequired；CancelPending只能在具体安全边界执行。 不自动重试擦写、不隐式启用Seed/Key；任何超时后不得假定ECU未写入；断电时标记RecoveryRequired；缺恢复资料拒绝实机。

**设计文件 10 项：**

- `src/AutoTest.Specialists/Flash/FirmwareImageParser.cs` → `FirmwareImageParser`；方法 `ParseHex/ParseS19/ValidateSegments`；分段镜像、校验与重叠检测。
- `src/AutoTest.Specialists/Flash/ProgrammingProfile.cs` → `ProgrammingProfile`；方法 `Validate/DescribeTarget`；目标/请求ID/块大小/安全策略/超时。
- `src/AutoTest.Specialists/Flash/ProgrammingPreflight.cs` → `ProgrammingPreflight`；方法 `CheckAsync/RejectMismatch`；ECU身份/文件/供电/会话/权限。
- `src/AutoTest.Specialists/Flash/IProgrammingWorkflow.cs` → `IProgrammingWorkflow`；方法 `BuildPlan/ExecuteAsync`；ECU专用阶段序列可注入。
- `src/AutoTest.Specialists/Flash/ProgrammingJobCoordinator.cs` → `ProgrammingJobCoordinator`；方法 `StartAsync/RequestCancel/Observe`；唯一Job状态所有者。
- `src/AutoTest.Specialists/Flash/ProgrammingStageHandlers.cs` → `ProgrammingStageHandlers`；方法 `EnterSession/Transfer/Verify/Reset`；按profile授权调UDS。
- `src/AutoTest.Specialists/Flash/FlashTransferScheduler.cs` → `FlashTransferScheduler`；方法 `WriteBlocks/CheckResponse`；分块、NRC、进度与失败记录。
- `src/AutoTest.Specialists/Flash/ProgrammingEvidence.cs` → `ProgrammingEvidence`；方法 `RecordStage/Finalize`；每次尝试/失败阶段可追溯。
- `src/AutoTest.Specialists/Flash/MockBootloader.cs` → `MockBootloader`；方法 `ReceiveRequest/SimulateNrc`；可注入掉电/延迟/错误的ECU仿真。
- `src/AutoTest.Desktop/Tools/FlashToolView.xaml` → `FlashToolView`；方法 `SelectProfile/Preflight/ShowStages`；独立烧录工具共享服务。

**参考源码/资料：**

- `CanTool:Config/FlashConfiguration.cs`
- `CanTool:Services/FirmwareParser.cs`
- `CanTool:Services/FlashSequence.cs`
- `CanTool:Protocol/SecurityAccess.cs`
- `CanTool:Services/FlashService.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Uds/QuickUdsService.cs`

**仍需通过实验关闭：** 缺少现场成功刷写对应配置、目标 ECU、HEX 和日志；这些是实机刷写的硬前置，不应成为G3伪造常数。

### FW1｜主控/按键固件版本、维护与回归接口

**工程：** `工程配置 / G4控制`；**G2：** G2-01/02/07/08；ICD-01/02/03

本轮不修改两套 MCU 固件，G3 定义后续固件缺口触发、接口变更和必要回归的设计合同。主控与按键板两个软件版本彼此独立，均必须可追踪到编译输入、烧录身份及 HOST兼容表。

**主要调用链**：固件修改建议→变更评审→独立源码改动与构建→现场烧录→版本证据→按影响选择 USB/CAN/IGN/按键回归→更新兼容表；不因HOST新增功能直接假设固件自动升级。

**状态/异常**：FirmwareCandidate→Built→Flashed→BenchVerified→Compatible；本轮现役固件工作状态保持受控冻结。 编译工程身份与磁盘 ZIP 哈希不等价于现场烧录身份；有差异先停机核对；严重无法软件解决的硬件缺口再提PCB变更。

**设计文件 3 项：**

- `docs/integration/FirmwareCompatibility.md` → `FirmwareCompatibility`；方法 `DefineSupportedPairs`；双固件/主控协议/Station能力兼容范围。
- `src/AutoTest.Devices/Station/FirmwareCompatibilityChecker.cs` → `FirmwareCompatibilityChecker`；方法 `Validate/ExplainLimitations`；连接后检查版本能力，不自动刷主控。
- `verification/FirmwareChangeImpact.md` → `FirmwareChangeImpact`；方法 `TraceChangedCommands/SelectRegression`；修订触及接口时重测B1～B6必要项。

**参考源码/资料：**

- `MainFW:Project/source/BSW/CDD/USB/CDD_USB.h`
- `MainFW:Project/source/ASW/BusProxy/ASW_BusProxy.c`
- `KeyFW:WS/EPB_EOL/Sources/APP/KeySim.c`

**仍需通过实验关闭：** 现场烧录的精确文件身份与编译Target需要用户回传或B0实测资料核对。

## 对应的表格登记

文件/类/方法、接口、步骤类型、测试、证据和问题均在本次交付 CSV 中有稳定 ID。主索引：`tables/SOURCE_EVIDENCE_INDEX.csv`。仅有正文但缺少对应表格或设计模块时，不能宣称该工作产品完成。

## Gate 限制

批准详细设计不等于通过对应单元测试、真实台架或完整生产烧录。

## 编程前必读的参考协议与接口原型

本交付附 `docs/REFERENCE_ANCHORED_PROTOCOL_CONTRACTS.md`（源码行、CRC、黄金向量）与 `docs/PUBLIC_API_SIGNATURES.md`（强类型接口候审原型），两份内容是本工作产品的配套设计约束。

## 附录 B · 基于现役双板固件的逐行协议证据

# 现役固件协议逐点证据与 G4 黄金向量

> **此文件只陈述从用户提供的源码静态得到的协议事实和按这些事实推导的测试向量。没有在真实硬件运行这些向量。**

## 1. 核查源代码的证据点

| 行为 | 来源与行号 | 校对结果 |
| --- | --- | --- |
| USB请求8B头尾 | `CDD_USB.h:27` | 请求为SOF+SEQ2+CMD+LEN+PAYLOAD+CRC2+EOF;无payload最短8B |
| USB响应9B头尾 | `CDD_USB.h:28` | 响应额外含 STATUS;无payload最短9B |
| USB CRC Modbus | `CDD_USB.c:44` | init=FFFF, reflected poly=A001;CRC覆盖SEQ至PAYLOAD，不含SOF/EOF |
| USB RX parser | `CDD_USB.c:65` | 分片与粘连、非法头尾/CRC自恢复；实际吞吐尚未复验 |
| CAN负载及检查 | `ASW_BusProxy.c:28` | 15字节:Channel/IDE/ID\_LE4/DLC/Data8;通道≤1,DLC≤8 |
| CAN发送响应 | `ASW_BusProxy.c:43` | 反映EAL\_CAN\_Send接受或繁忙，不证明DUT收到/动作 |
| CAN RX异步 | `ASW_BusProxy.c:75` | SEQ=0 CMD=41;可能与正常命令回包交错 |
| Key CAN ID | `KeySim.c:5` | 按键板仅接收0x730的8B标准数据帧 |
| Key mask | `KeySim.c:44` | Data1+2+Data3低4位拼20bit，Data3高4位必须为0 |
| Key release | `KeySim.c:38` | 释放全部；余字节必须为0；无独立物理回执 |

## 2. USB 帧及 CAN 载荷：必须按主控实际源码实现

**主控 USB 请求帧**：`A5 SEQ_LE2 CMD LEN PAYLOAD[LEN] CRC16_LE2 5A`；**主控响应帧**：`A5 SEQ_LE2 CMD STATUS LEN PAYLOAD[LEN] CRC16_LE2 5A`。CRC 使用 Modbus 反射算法（INIT FFFF、POLY A001），计算范围只含 `SEQ…PAYLOAD`。

**CAN\_SEND 0x40**：15B `CAN port, IDE, ID LE32, DLC, data[8]`。标准 CAN ID `0x730` 按 4 字节LE编码 `30 07 00 00`。按键板接收 8B `01 mask_low mask_mid mask_high4 00 00 00 00`，释放全部使用 `80 00 00 00 00 00 00 00`。

**CAN\_RX\_IND 0x41**：SEQ=0 的无请求异步上报，结构为响应帧，15B CAN payload。CAN发送返回 OK 仅表示 MCU CAN 调用成功，**不证明从站收到了命令，更不证明动作已发生**。

### 从现役固件格式推导的 G4 测试向量

| 向量 | 十六进制 | 状态 |
| --- | --- | --- |
| USB-REQ-PING | `A5 34 12 F0 00 EA 11 5A` | DERIVED / NOT\_RUN |
| USB-RESP-PING | `A5 34 12 F0 00 00 90 8F 5A` | DERIVED / NOT\_RUN |
| CAN-SEND-KEY1 | `00 00 30 07 00 00 08 01 01 00 00 00 00 00 00` | DERIVED / NOT\_RUN |
| USB-CAN-SEND-KEY1 | `A5 37 00 40 0F 00 00 30 07 00 00 08 01 01 00 00 00 00 00 00 A8 83 5A` | DERIVED / NOT\_RUN |
| KEYPAD-SET-KEY1 | `01 01 00 00 00 00 00 00` | DERIVED / NOT\_RUN |
| KEYPAD-RELEASE-ALL | `80 00 00 00 00 00 00 00` | DERIVED / NOT\_RUN |
| USB-CAN-RX-EVENT | `A5 00 00 41 00 0F 00 00 30 07 00 00 08 01 01 00 00 00 00 00 00 27 30 5A` | DERIVED / NOT\_RUN |

## 3. 与硬件实测直接相关的停止条件

- 真实 PC 端口与固件运行身份必须先 B0/B1 确认；即使源码内有旧电源命令，也不得实现虚假的 `dut.power` 和 `keypad.power`。
- 当前固件对 IGN 的初始输出可能存在上电使能情形，B0 必须先检查安全与物理电平，不让连接成功自动发送控制命令。
- 0x730 报文发送成功不证明按键板识别；KEY16 的电阻网络/引脚对应还需要 B4 测量确认。
- 夹具无到位传感器；收到 USB ACK 或 ECU CAN 报文都不能记成直接的夹紧到位检测。
- CAN0/CAN1是固件通道编号，实物接头映射仍须现场验证。

## 4. G4 执行前的双人审查规则

在实施具体 USB/CAN/按键代码前，开发者必须校核本文件与 `SOURCE_EVIDENCE_INDEX.csv` 中的源 SHA256；存在内容不一致、现场协议/版本不一致时，暂停当前接口实施、提交差异报告，不得猜一个兼容值让实测失败再修。

### 41项源码文件哈希索引

| 证据资产 | 源文件相对路径 | SHA256 | 代码/事实用途 | 使用边界 | 证据等级 |
| --- | --- | --- | --- | --- | --- |
| MainFW | Project/source/BSW/CDD/USB/CDD\_USB.h | 1796abdc83ca3c431bbe7b26b786c5dbed8aa8ad77ae37a5a3cd1b2002397a51 | USB请求/响应、命令字节常量 | 固件源码可查；现场运行待核 | STATIC\_REVIEW |
| MainFW | Project/source/BSW/CDD/USB/CDD\_USB.c | cbd342a60d14806f989bb7ae1d538640569b77535c8dc1cdf6e801b06d9d9a1a | 帧解码与CRC | 固件源码可查；需黄金帧验证 | STATIC\_REVIEW |
| MainFW | Project/source/ASW/UsbSvc/ASW\_UsbSvc.c | 2c7ecee0b51ead03afba5d620eea8a3b6387fbc8634459571ea1d38465f5aff3 | 命令路由、USB与CAN代理注册 | 固件源码可查；参与编译待最终确认 | STATIC\_REVIEW |
| MainFW | Project/source/ASW/BusProxy/ASW\_BusProxy.c | 4dc262db1426c1e49f6b11ea60224dfe9279144f4d6fb50b17503d17a5665e35 | CAN 15B载荷、CAN0/CAN1映射 | 代码依据；物理对应实测 | STATIC\_REVIEW |
| MainFW | Project/source/ASW/PwrCtrl/ASW\_PwrCtrl.c | d77e84a0f7aeac9e2ce853e25f7eead397320ad8b9d50e9c21a8b0192468932a | IGN/电磁阀控制代码 | 电气输出需B0/B3 | STATIC\_REVIEW |
| KeyFW | WS/EPB\_EOL/Sources/APP/KeySim.c | c6ea49990609bc55124e5b7c22b3d63c0c8f6a0821f308c08ab18c2b2ad212d6 | 0x730/0x01/0x80/20位mask | 代码依据；实际KEY16需B4 | STATIC\_REVIEW |
| KeyFW | WS/EPB\_EOL/Sources/APP/main.c | 6812681b7df42cec286e9d3cccacea627e7fdd72464b9ef0851bb1ab9baaf5e1 | 按键接收处理主循环 | 代码依据；现场固件身份待核 | STATIC\_REVIEW |
| KeyFW | WS/EPB\_EOL/Sources/BSW/CAN/Driver\_CAN.c | 7a22531c48a92bb1fd7fb4f003da62f918deba0ccb75d395490d6fc3e45db168 | 接收队列与溢出计数 | 代码依据；实际压力待测 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Integration.Usb/UsbProtocol.cs | 6deab99bb3362b2483378d21f4be8e8fb1392639fb476ccd632e10fcb7f15601 | 旧USB codec与会话设计 | 研究参考非新架构 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Integration.Usb/UsbSession.cs | 759e2559ef92945d032e5dea942058f5ae7da2d311b89f2d91a6f100ab454e0d | 旧request关联与超时 | 研究参考非实测证明 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Adapters.MainController/MainControllerCanAndKeyCapabilities.cs | 51ff62f50cd9502ce6addfe79d0d22204ba6f36b078c9cb8a32c794a570b1a58 | 旧能力适配 | 需过滤过期电源假设 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Communication.Can/CanSubscription.cs | 0ce62a2e982aa5230cba45c4eeceff7eef10dde4977b1124500ec92d5a218df0 | CAN订阅模型 | 可提炼机制 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Communication.Can.Usb/CanUsbPayloadCodec.cs | 642bd3be59c89cba73e6020f904d1676d618675255c113bad6f1f0bd4fb4e33a | CAN字节编解码 | 用于校对固件 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Workspace/WorkspaceManifest.cs | 0fa06c3e0679502e11f85feadfba9d23986f3ab3c03659418870fcc0851107b9 | 资源/清单模型 | 参照设计，不直接沿用格式 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Workspace/FileAssetStore.cs | 9dc9dd920066174e07c279242251dbb251a0155c1044bdc1346f168d0dfbe729 | 资产存储 | 参考文件往返与依赖 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Resources.Runtime/CapabilityBindingResolver.cs | 738090cffbc91f4e6b29776d9cfd4f91224800760d2e306994c9f4ffca6c5fdf | 资源端点绑定 | 结构研究成果 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Testing.Authoring/AuthoringTransactionEngine.cs | e67116c27c082cb49ace7ce00fe349d86c0996bddc162195a81a1e6478be893a | 编辑事务 | 强重点研究 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Testing.Authoring/UndoRedoHistory.cs | 6a6556e6320046d892ff0a43845298f9ed1c0da9ad094a253a84ae92525000e4 | 撤销重做 | 强重点研究 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Testing.Authoring/NodeTypeRegistry.cs | f6f0b9cfe794e9ee8379869c1b22bdabaddf2a4b1d9f70937c03064d219ed2d5 | 节点注册 | 强重点研究 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Testing.Runtime/Preparation/L5RunPreparation.cs | 869c497fee75ebbd700f200d92966689ed25065b42b710f7087f18a10453c14a | 执行前准备 | 研究实现思路 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Testing.Runtime/Execution/RunExecutionKernel.cs | c87924c86d113480c9c2936861ff70d053296c457b8354609be68821366d4167 | 执行内核 | 研究实现思路 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Testing.Runtime/Execution/RunControlCoordinator.cs | fadbbae0ff479d4ac8ba816fc770727eeb35a3ecf6a8d880467f4001075d2200 | 运行控制 | 研究实现思路 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Communication.Dbc/DbcDatabase.cs | 5f230909c3ffc9830a0d9310fc4d1d84809f722e7e8811fd57ef327fdcfa95f0 | DBC语义模型 | 研究参考 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Communication.Dbc/Authoring/DbcAuthoringSession.cs | f08452bd487b32c3ca5c985f87224659c5c01c5ed6af8d210d41134599736546 | DBC编辑会话 | 研究参考 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Communication.Dbc/Authoring/DbcSourcePatchWriter.cs | ed830c282b414dbea44ef0760ca5378b4a3a80153293d6d1d32f02041c22429a | DBC改写 | 研究参考 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.QuickDesktop/Uds/QuickUdsService.cs | cad0678a059a9ccbe113417953a85e82c0be659af4ed54392652fc3aee8abc04 | 有限UDS请求/响应 | 只能作为部分能力参考 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.QuickTesting/Waveform/QuickWaveformCaptureEngine.cs | 8d6ef68ca3e6f71155fd1393a8e573dae6c473292bf332be346a908d1de535dc | 波形采集 | 研究参考/要测质量 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.QuickTesting/Waveform/QuickWaveformComparator.cs | fdea7ce9375591b37bce5b9cd152aa83621c42752fe9f364c8352becf89e8f24 | 波形对齐与匹配率 | 不能跳过真波形标定 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.QuickTesting/Waveform/QuickReferenceWaveformCodec.cs | 280d5f8543a28f2c4442b9940a2b12dc2e47cd333fe1abcaa6f2815ca403bfc9 | 参考波形资源 | 研究参考 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Records.Sqlite/SqliteExecutionRecordStore.cs | 96c7946bfb7c33cb7ebb85a74475fa91930804d1967e67b220c7abfbe8577039 | SQLite记录 | 数据恢复与不覆写必须验证 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.Records/ExecutionRecordBuilder.cs | 6b409362a4656c7cdbee0b5bdde28adfae92e5c6b586b23dd8168d2f0f67972c | 记录构建 | 研究参考 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.QuickDesktop/Themes/Palette.xaml | 731f6e997d0ed46a3db715943088ea940ca5cc20db22419a47518e45a5a79ba0 | 视觉配色 | 可提炼风格，不继承旧Shell | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.QuickDesktop/Themes/TreeTable.xaml | 77f5e20e15e67056cc48604c36765d16d90e84ca70c4215bec1dceb10629701b | 树表风格 | 视觉候选 | STATIC\_REVIEW |
| HistoricalHOST | src/AutoTest.QuickDesktop/Themes/Inspector.xaml | 25f976e50b7cb11b87318e8bdf0bf0a1ae01bc0d0ab3a796b71647d9f8c4596e | 属性编辑风格 | 视觉候选 | STATIC\_REVIEW |
| CanTool | Config/FlashConfiguration.cs | cdd2460ca434fd747d4a5311c777f357b7ef1c7004d8e7a6e95e524291e4facf | 烧录配置对象 | 功能参考非默认配置 | STATIC\_REVIEW |
| CanTool | Services/FirmwareParser.cs | 930433c90eb1726a2831816aa0e0fd12113f4890e6b24176da0b7ae61b95892b | HEX/S19解析 | 功能参考需独立测试 | STATIC\_REVIEW |
| CanTool | Services/FlashSequence.cs | d94c884449f140fe244334bac0674839bb6262f443c625a6c6ab94e5c5249ce9 | UDS烧录状态/序列 | 专用流程不可直接移植 | STATIC\_REVIEW |
| CanTool | Protocol/UdsTransport.cs | e760209eea65165ed8a388da85c1470d2454295c583a35ffaf9dce9eec7494d0 | UDS传输与NRC | 领域参考，适配新CAN | STATIC\_REVIEW |
| CanTool | Protocol/SessionManager.cs | 6d47784525944c21ad6634980ae20854b0f1510075a9a31ef6a9f0cdebaba7bc | 诊断会话转换 | 领域参考 | STATIC\_REVIEW |
| CanTool | Protocol/SecurityAccess.cs | bfe7702a4e38b3c17fc73d1527c7f36b1870639cd64f4b76ec25cf390ae6c583 | Seed/Key执行逻辑 | 具体算法必须按目标授权 | STATIC\_REVIEW |
| CanTool | Services/FlashService.cs | eefbde8d66b75a46945bad1bf581c49cfb3487e028306572b12af0fb33f589ed | 烧录对外服务 | 领域参考 | STATIC\_REVIEW |

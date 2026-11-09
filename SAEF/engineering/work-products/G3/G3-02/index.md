# G3-02｜类、接口、关键函数（全量候审 v0.2）

> SAEF G3 正式候审内容；**G4尚未实施、所有测试初始 NOT\_RUN**。

## 上游批准约束

G2 v0.7 的系统边界/12 项 ICD/状态所有权及 P0～G1 已批准内容必须保留。详见 `README_FIRST.md` 和 `docs/COMMON_DESIGN_CONTRACT.md`。

## 本工作产品适用的详细设计模块

### G3-0｜公共模型、依赖与变更合同

**工程：** `Core / Contracts / Application`；**G2：** G2-03/04/05/06；ICD-01～12

核心与接口仅定义一次：身份强类型、运行与会话世代、错误分层、单调时间、证据、取消与授权语义。域服务不得私自复制 DTO。九个生产程序集是本次详细设计的组织决定；15 个 G2 逻辑组件独立于程序集边界。

**主要调用链**：View/VM → Application 命令 → 领域接口 → 设备/持久化；观测沿事件/只读 DTO 逆向投影。跨模块 DTO 限制在 Contracts，设备协议私有类型不外泄。

**状态/异常**：OperationId 每次操作唯一；SessionGeneration 每次重连变化；RunSnapshot 和 PreparedPlan 启动后不可变；错误域与证据质量不可压缩为 bool。 DTO 验证失败：Rejected；设备操作超时：UncertainAfterSideEffect；无证据：Unknown；记录落盘失败：Uncommitted。

**设计文件 12 项：**

- `src/AutoTest.Core/Ids.cs` → `StationId,EndpointId,ProjectId,AssetId,TestDocumentId,StepId,RunId`；方法 `Create/TryParse/ToString`；不可混用的类型化ID。
- `src/AutoTest.Core/OperationResult.cs` → `OperationResult<T>,OperationFailure`；方法 `Accepted/Rejected/Uncertain`；请求接受、设备应答与真实观测不同层级。
- `src/AutoTest.Core/TimeEvidence.cs` → `EvidenceRef,TimeQuality,TimePoint`；方法 `FromHostArrival/FromReplay`；记录时源、质量、采集来源。
- `src/AutoTest.Core/VersionFingerprint.cs` → `RevisionFingerprint`；方法 `FromSha256/Matches`；资源与程序按内容识别。
- `src/AutoTest.Contracts/DeviceContracts.cs` → `IStationSession,IStationControl`；方法 `ConnectAsync/ExecuteAsync`；设备层统一入口。
- `src/AutoTest.Contracts/CanContracts.cs` → `ICanFrameHub,ITransmitLeaseService`；方法 `SubscribeAsync/TransmitAsync`；单接收流与租约。
- `src/AutoTest.Contracts/ProjectContracts.cs` → `IProjectService,IAssetResolver`；方法 `OpenAsync/Resolve/SaveAsync`；工程只读快照与资源验证。
- `src/AutoTest.Contracts/TestingContracts.cs` → `ITestAuthoringService,IPlanCompiler,IRunController`；方法 `Apply/PrepareAsync/StartAsync`；编辑与执行不互引UI。
- `src/AutoTest.Contracts/SpecialistContracts.cs` → `IBarcodeInput,IUdsClient,IWaveformService,IProgrammingService`；方法 `Capture/Execute/Compare/Program`；专业入口分开，共享CAN。
- `src/AutoTest.Contracts/RecordContracts.cs` → `IRunFactSink,IRecordReader`；方法 `AppendAsync/CommitAsync/QueryAsync`；追加事实与可验证提交。
- `src/AutoTest.Contracts/UiContracts.cs` → `IApplicationCommandGateway,WorkbenchProjection`；方法 `ExecuteAsync/Observe`；UI只用命令和只读投影。
- `src/AutoTest.Application/Composition/ApplicationComposition.cs` → `ApplicationComposition`；方法 `RegisterCore/RegisterMock/RegisterReal`；唯一装配根/防止双设备句柄。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Capabilities.Contracts/AutoTest.Capabilities.Contracts.csproj`
- `HistoricalHOST:src/AutoTest.Testing.Runtime.Contracts/L5RuntimeContracts.cs`
- `HistoricalHOST:src/AutoTest.Application/Ports/ApplicationPorts.cs`

**仍需通过实验关闭：** P0/G2 已冻结的是职责；本轮定义名称和签名。外部 NuGet 版本以目标 Windows 工位的 SDK 与离线恢复实验验证，不伪称依赖已安装。

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

### B1｜工程、资源身份、文件持久化

**工程：** `AutoTest.Projects`；**G2：** G2-03/05/06；ICD-06

建立清晰的项目文件容器：项目元数据、资源清单、运行配置、DBC、测试程序、HEX引用、烧录Profile、参考波形、编号匹配规则；不把历史运行记录当可覆盖的工程资源。支持损坏/缺引用明确拒绝与旧版本迁移审计。

**主要调用链**：打开工程→读取manifest→验证schema/路径/内容哈希→构建索引→引用诊断；保存时先将资源写入临时文件并校验，再提交新revision，不静默覆盖历史资产。

**状态/异常**：Closed→Opening→Loaded/Corruped→Dirty→Saving→Loaded/SaveFailed；工程工作区与真实设备连接是独立状态。 缺文件/哈希不一致禁止编译Run；同名不同资源区分；工程中 HEX 文件可引用或导入但运行必须有内容指纹；取消保存不产生半成品。

**设计文件 10 项：**

- `src/AutoTest.Projects/Project/ProjectDefinition.cs` → `ProjectDefinition`；方法 `Validate/WithRevision`；ProjectId、资源清单与格式版本。
- `src/AutoTest.Projects/Project/ProjectRepository.cs` → `ProjectRepository`；方法 `Create/OpenAsync/SaveAsync`；暂存写入+校验+原子替换或明确失败。
- `src/AutoTest.Projects/Project/ProjectPathPolicy.cs` → `ProjectPathPolicy`；方法 `Normalize/ValidateInsideRoot`；阻止资源路径越界。
- `src/AutoTest.Projects/Resources/AssetEntry.cs` → `AssetEntry`；方法 `Create/VerifyHash`；AssetId、Revision、Kind、SHA256。
- `src/AutoTest.Projects/Resources/AssetCatalog.cs` → `AssetCatalog`；方法 `Import/ReplaceRevision/Resolve`；统一资产身份不依赖文件名。
- `src/AutoTest.Projects/Resources/ReferenceValidator.cs` → `ReferenceValidator`；方法 `ScanDependencies/RejectDangling`；DBC、测试步骤与配置引用检查。
- `src/AutoTest.Projects/Resources/ResourceEditService.cs` → `ResourceEditService`；方法 `Add/Rename/Remove/Undo`；工程树操作事务。
- `src/AutoTest.Projects/Profiles/DutAndStationProfiles.cs` → `DutProfile,StationProfileRef`；方法 `ResolveProfile/CheckVoltage`；型号配置与电源前置关系。
- `src/AutoTest.Projects/Profiles/ResourceBindingService.cs` → `ResourceBindingService`；方法 `Resolve/ValidateSnapshot`；逻辑端点与能力引用。
- `src/AutoTest.Projects/Storage/SchemaMigrationService.cs` → `SchemaMigrationService`；方法 `Preview/MigrateCopy`；版本迁移可预检与失败停机。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Workspace/WorkspaceManifest.cs`
- `HistoricalHOST:src/AutoTest.Workspace/FileAssetStore.cs`
- `HistoricalHOST:src/AutoTest.Workspace/AssetDocumentCodec.cs`
- `HistoricalHOST:src/AutoTest.Resources.Runtime/CapabilityBindingResolver.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Infrastructure/ResourceRemovalDependencyInspector.cs`

**仍需通过实验关闭：** 当前提议 project.json + assets/ 按类型归档、记录存独立记录库；最终格式需要一次Mock工程往返检验与真实UI试用，但基础资源身份不变。

### B2｜DBC解析、信号定义与数值转换

**工程：** `AutoTest.Projects`；**G2：** G2-03/05/06；ICD-06/12

以 DBC 资源修订为唯一信号定义来源，解析帧ID、IDE、位序、字节序、缩放、偏移、signed、枚举/多路复用；SignalRef 附带 DBC revision。编辑 DBC 的高级能力沿用旧研究但不阻碍首版只读导入与查询。

**主要调用链**：资源目录→DBC parse→SignalRef→CAN帧→数值解码和单位→CAN/波形/测试订阅；工程内 DBC 编辑要产生新版本并核查受影响步骤。

**状态/异常**：Parse: Valid/Warnings/Invalid；Signal: Fresh/Stale/Unavailable/Lossy；错误信号不允许默认0合格。 错误位序、数据长度、未定义信号、过期修订/多路复用条件缺失必须明确；实际报文与 DBC 对不上不写成设备故障。

**设计文件 7 项：**

- `src/AutoTest.Projects/Dbc/DbcSource.cs` → `DbcSource`；方法 `Load/Hash`；保留源文件与编码诊断。
- `src/AutoTest.Projects/Dbc/DbcDatabase.cs` → `DbcDatabase`；方法 `FindMessage/FindSignal`；信号与报文不可变定义。
- `src/AutoTest.Projects/Dbc/DbcParserAdapter.cs` → `DbcParserAdapter`；方法 `Parse/TryParse`；DBC语义解析与错误位置。
- `src/AutoTest.Projects/Dbc/DbcSignalCodec.cs` → `DbcSignalCodec`；方法 `DecodeRaw/EncodeRaw/ToPhysical`；明确Intel/Motorola位序。
- `src/AutoTest.Projects/Dbc/DbcSignalResolver.cs` → `DbcSignalResolver`；方法 `Resolve(SignalRef)/DetectStale`；防修订漂移。
- `src/AutoTest.Projects/Dbc/DbcEditSession.cs` → `DbcEditSession`；方法 `Apply/Validate/Export`；高级编辑可演进的事务边界。
- `src/AutoTest.Projects/Dbc/DecodedSignalPublisher.cs` → `DecodedSignalPublisher`；方法 `OnCanFrame/ObserveSignal`；复用 CAN Hub 输入和质量标记。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Communication.Dbc/DbcDatabase.cs`
- `HistoricalHOST:src/AutoTest.Communication.Dbc/Authoring/DbcAuthoringSession.cs`
- `HistoricalHOST:src/AutoTest.Communication.Dbc/Authoring/DbcSourcePatchWriter.cs`
- `HistoricalHOST:src/AutoTest.QuickTesting/Signals/QuickDbcSignalResolver.cs`

**仍需通过实验关闭：** 真实EPB DBC中电机电流的具体帧/信号和广播周期，需要导入用户提供的实际 DBC 并在 B2 实测。

### C1｜测试程序编辑、节点目录、流程控制

**工程：** `AutoTest.Testing`；**G2：** G2-03/04/05/06；ICD-07

编辑文档是独立资源，节点有稳定StepId、类型、参数、子槽位、注释和引用；编辑以事务处理支持 Undo/Redo；步骤类型及属性由同一 Descriptor 供编辑器与编译器读取。避免 UI 各自定义运行语义。

**主要调用链**：用户新增资源（工程域）与添加步骤（测试域）分别走不同命令；新建测试程序→追加/编辑→事务检查→保存资源→准备运行时从保存版本编译。

**状态/异常**：Clean→Dirty→Validating→Saved/Error；SelectedStep只由UI拥有；正在运行Step属于Runtime。 非法节点/循环、坏SignalRef、被删除资源都指向StepId+属性；复制节点生成新ID；编辑不会偷偷改运行中的计划。

**设计文件 8 项：**

- `src/AutoTest.Testing/Authoring/TestDocument.cs` → `TestDocument,StepNode`；方法 `Create/CloneSnapshot`；可编辑树，稳定StepId。
- `src/AutoTest.Testing/Authoring/StepDescriptorCatalog.cs` → `StepDescriptorCatalog`；方法 `Register/Lookup/Enumerate`；类型/属性/资源要求/槽位元数据。
- `src/AutoTest.Testing/Authoring/StepEditTransaction.cs` → `StepEditTransaction`；方法 `Apply/Undo/Redo`；插入移动复制删除的原子操作。
- `src/AutoTest.Testing/Authoring/StepTreeValidator.cs` → `StepTreeValidator`；方法 `ValidateTree/ValidateSlots`；禁止循环引用与非法Else位置。
- `src/AutoTest.Testing/Authoring/TestDocumentCodec.cs` → `TestDocumentCodec`；方法 `Serialize/Deserialize/Migrate`；持久化与版本诊断。
- `src/AutoTest.Testing/Authoring/StepPropertyValidator.cs` → `StepPropertyValidator`；方法 `ValidateBinding/ValidateParameter`；属性面板编辑前验证。
- `src/AutoTest.Testing/Authoring/StepCatalogRegistration.cs` → `StepCatalogRegistration`；方法 `RegisterBuiltins`；全部工作包共用步骤注册入口。
- `src/AutoTest.Testing/Authoring/StepRefactorService.cs` → `StepRefactorService`；方法 `RenameAsset/RemapReference`；工程资源变化传播检查。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Testing.Authoring/AuthoringTransactionEngine.cs`
- `HistoricalHOST:src/AutoTest.Testing.Authoring/UndoRedoHistory.cs`
- `HistoricalHOST:src/AutoTest.Testing.Authoring/NodeTypeRegistry.cs`
- `HistoricalHOST:src/AutoTest.Testing.Domain/AuthoringModel.cs`

**仍需通过实验关闭：** 流程控制（If/Else/Loop/Parallel）必须有统一语义，但首批WPF编辑交互形式通过真实用户试用确定；Parallel先以资源独占校验限制安全。

### C2｜测试计划、执行、步骤判定和运行事实

**工程：** `AutoTest.Testing`；**G2：** G2-03/04/05/07；ICD-07/08/10

运行前根据 TestDocument+项目+Station 能力冻结 PreparedPlan；启动生成独立 RunSnapshot；步骤 Handler 使用专业能力接口，不能直接读可变ViewModel。结果区分 PASS/FAIL/INCONCLUSIVE/ERROR/SKIPPED，记录实际观测与来源。

**主要调用链**：Document→PlanCompiler→PreparedPlan→RunSnapshot→Handle+Lease→Command/Observation→Judge→RunFact→Records→UI投影。If/Else和Loop在计划结构化节点求值；Parallel不允许未仲裁的资源竞争。

**状态/异常**：Idle→Preparing→Ready→Running→PausePending/Paused/StopPending→Finalizing→Finished/Failed/Interrupted；Step状态 Pending/Running/Passed/Failed/Inconclusive/Skipped/Error。 未得物理证据返回Unknown或Inconclusive；USB断连取消后续副作用；正在擦写不能假定可暂停；RunFact落盘失败仍保留未归档告警。

**设计文件 10 项：**

- `src/AutoTest.Testing/Preparation/PlanCompiler.cs` → `PlanCompiler`；方法 `PrepareAsync/ValidateDependencies`；冻结步骤图、绑定与诊断。
- `src/AutoTest.Testing/Preparation/PreparedPlan.cs` → `PreparedPlan`；方法 `ValidateFingerprint`；只读执行快照。
- `src/AutoTest.Testing/Runtime/RunCoordinator.cs` → `RunCoordinator`；方法 `StartAsync/RequestStop/PauseAtBoundary`；运行唯一状态拥有者。
- `src/AutoTest.Testing/Runtime/StepDispatcher.cs` → `StepDispatcher`；方法 `DispatchAsync`；按StepDescriptor查找Handler。
- `src/AutoTest.Testing/Runtime/StepHandlerRegistry.cs` → `StepHandlerRegistry`；方法 `Resolve/Register`；可替换Handler与版本合同。
- `src/AutoTest.Testing/Runtime/FlowExecutors.cs` → `IfExecutor,LoopExecutor,ParallelExecutor`；方法 `ExecuteAsync`；明确条件求值及有界循环。
- `src/AutoTest.Testing/Runtime/ResourceLeaseScope.cs` → `ResourceLeaseScope`；方法 `AcquireAsync/DisposeAsync`；操作结束释放资源租约。
- `src/AutoTest.Testing/Runtime/StepJudge.cs` → `StepJudge`；方法 `Evaluate/NoEvidence`；可追踪预期/实际与质量。
- `src/AutoTest.Testing/Runtime/RunFactJournal.cs` → `RunFactJournal`；方法 `Append/Publish`；运行事实追加且带OperationId。
- `src/AutoTest.Testing/Runtime/RunControlPolicy.cs` → `RunControlPolicy`；方法 `CanPause/CanSkip/CanStep`；不可中断副作用的安全边界。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Testing.Runtime/Preparation/L5RunPreparation.cs`
- `HistoricalHOST:src/AutoTest.Testing.Runtime/Execution/RunExecutionKernel.cs`
- `HistoricalHOST:src/AutoTest.Testing.Runtime/Execution/RunControlCoordinator.cs`
- `HistoricalHOST:src/AutoTest.Testing.Runtime/Execution/RunCapabilityScope.cs`
- `HistoricalHOST:src/AutoTest.QuickTesting/Runtime/QuickSignalCheckHandler.cs`

**仍需通过实验关闭：** 首版步骤支持优先以真实使用反馈确定；具体判定阈值不在G3写死。

### D1｜WPF 视觉系统、主题与组件画廊

**工程：** `AutoTest.Desktop`；**G2：** G2-03/05；ICD-11

视觉不仅是配色：统一 Color/Type/Spacing/Density/Icon/States/Contrast Token；资源字典提供控件模板，不在业务页面硬编码视觉值；组件画廊要能独立运行并接受用户截图反馈。

**主要调用链**：Token→ResourceDictionary→ControlTemplate→业务View；主题切换只替换资源层，不修改设备状态与业务VM。

**状态/异常**：Theme=Light/Dark；Density=Compact/Normal；控件状态 Normal/Hover/Focused/Selected/Disabled/Invalid/Running/Passed/Failed/Unknown。 颜色不可作为唯一状态依据；缩放125/150/200%、长中文属性、英文标识与窄窗口不允许内容被剪掉；图标资源加载失败要有文字替代。

**设计文件 10 项：**

- `src/AutoTest.Desktop/Themes/Tokens/Colors.xaml` → `ColorTokens`；方法 `Primary/Surface/Status`；基础色与状态色分离。
- `src/AutoTest.Desktop/Themes/Tokens/Typography.xaml` → `TypographyTokens`；方法 `FontSize/Weight`；导航/内容/属性/表格字体体系。
- `src/AutoTest.Desktop/Themes/Tokens/Metrics.xaml` → `MetricTokens`；方法 `Compact/Normal`；高密度UI尺寸。
- `src/AutoTest.Desktop/Themes/Light.xaml` → `LightTheme`；方法 `MergedDictionaries`；浅色主题资源映射。
- `src/AutoTest.Desktop/Themes/Dark.xaml` → `DarkTheme`；方法 `MergedDictionaries`；深色主题资源映射。
- `src/AutoTest.Desktop/Themes/Controls/TreeStyles.xaml` → `TreeStyles`；方法 `Selection/Hover/RunState`；工程树/步骤树视觉状态。
- `src/AutoTest.Desktop/Themes/Controls/Inputs.xaml` → `InputStyles`；方法 `Validation/Disabled`；字段编辑视觉及错误反馈。
- `src/AutoTest.Desktop/Themes/Controls/Tables.xaml` → `TableStyles`；方法 `RowVirtualization`；CAN日志/结果/统计高密度表。
- `src/AutoTest.Desktop/Themes/Controls/RunBadges.xaml` → `RunBadges`；方法 `Passed/Failed/Unknown`；状态图标和文字配合。
- `src/AutoTest.Desktop/DesignGallery/GalleryWindow.xaml` → `GalleryWindow`；方法 `PreviewTheme/PreviewDensity`；组件/文字/状态可视化验收。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.QuickDesktop/Themes/Palette.xaml`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Themes/TreeTable.xaml`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Themes/Inspector.xaml`

**仍需通过实验关闭：** 初版视觉方向由用户认可，但尺寸/信息密度/操作反馈需真实WPF运行后调整；静态HTML不能替代WPF验收。

### D2｜Prism 工作台、编辑器及专业工具窗口

**工程：** `AutoTest.Desktop / AutoTest.Application`；**G2：** G2-03/04/05；ICD-11

统一 HOST：工程树+中央文档+右侧属性+底部日志/运行结果；研发与生产使用同一核心服务，工作空间是布局与权限投影，不是两套功能实现；专业CAN/UDS/烧录/波形采用独立可操作工具窗口或文档视图。

**主要调用链**：按键/菜单→VM命令→Application→领域服务→只读投影回VM；UI操作“+新建资源”与“+添加步骤”分离；选中步骤只触发属性展示，运行状态独立更新。

**状态/异常**：Selection(Asset/Step/RunFact)；Document(Dirty/Saved/Conflict)；Window(Open/Hidden/Detached)；RunView按事实展示非自造状态。 设备断连禁用危险按钮并显示原因；无法编辑不做假控件；保存失败保留Dirty；界面被关闭不取消不可中断的操作。

**设计文件 14 项：**

- `src/AutoTest.Desktop/App.xaml` → `App`；方法 `Startup/Resources`；唯一WPF启动和Theme字典。
- `src/AutoTest.Desktop/Bootstrap/PrismRegistration.cs` → `PrismRegistration`；方法 `RegisterTypes/CreateShell`；Prism 9导航、容器组合。
- `src/AutoTest.Desktop/Shell/MainWindow.xaml` → `MainWindow`；方法 `Document/Left/Right/BottomRegions`；IDE工作台。
- `src/AutoTest.Desktop/Shell/MainWindowViewModel.cs` → `MainWindowViewModel`；方法 `OpenCommand/RunCommand`；只发送应用命令。
- `src/AutoTest.Desktop/Shell/WorkbenchLayoutService.cs` → `WorkbenchLayoutService`；方法 `SaveLayout/RestoreLayout`；窗口与Dock状态保存。
- `src/AutoTest.Desktop/Shell/SelectionContext.cs` → `SelectionContext`；方法 `Select/GetSelection`；选中项与当前运行步骤分离。
- `src/AutoTest.Desktop/Project/ProjectExplorerView.xaml` → `ProjectExplorerView`；方法 `AddResource/Rename/Delete`；工程导航和操作。
- `src/AutoTest.Desktop/Testing/TestEditorView.xaml` → `TestEditorView`；方法 `SelectStep/InsertStep`；树/步骤双击及右键编辑。
- `src/AutoTest.Desktop/Testing/StepInspectorView.xaml` → `StepInspectorView`；方法 `EditProperty/Validate`；右侧属性编辑和即时诊断。
- `src/AutoTest.Desktop/Testing/RunMonitorView.xaml` → `RunMonitorView`；方法 `ObserveRun/ShowEvidence`；预期/实际/判定/步骤状态。
- `src/AutoTest.Desktop/Devices/BenchDiagnosticsView.xaml` → `BenchDiagnosticsView`；方法 `Connect/ShowRawFrames`；USB及台架复验入口。
- `src/AutoTest.Desktop/Tools/ToolWindowRegistry.cs` → `ToolWindowRegistry`；方法 `OpenCan/OpenUds/OpenFlash/OpenWave`；共享专业窗口生命周期。
- `src/AutoTest.Desktop/Production/ProductionWorkspaceView.xaml` → `ProductionWorkspaceView`；方法 `Capture/Preflight/Run`；生产工作区与结果。
- `src/AutoTest.Application/Commands/ApplicationCommandRouter.cs` → `ApplicationCommandRouter`；方法 `ExecuteAsync/GetCanExecuteReason`；跨模块命令与失败反馈。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.QuickDesktop/Shell/WorkspaceModeController.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Editor/NodeEditors/CanSendNodeEditor.xaml.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/CanTrace/CanTracePaneView.xaml.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Uds/UdsDebugWindow.xaml.cs`

**仍需通过实验关闭：** 具体 Dock 库是否采用第三方必须在 G4 小型WPF原型对比许可证与可用性；不要求全部 UI 操作现在永久冻结。

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

### R1｜运行事实、历史记录、查询与统计

**工程：** `AutoTest.Records`；**G2：** G2-04/05/06；ICD-10

每个 RunId 独立、不可覆盖；结果 Verdict 与记录提交状态分开；每步操作和证据带关联ID；生产统计区分 RunCount、DistinctDutCount、按最终完成情况统计。旧src已有SQLite与artifact研究，需检验恢复与一致性。

**主要调用链**：Runtime/Programming→AppendFacts→事务提交→CommitReceipt→History/Statistics；失败记录应当保留尝试事实，即使测试 Verdict 无法归档也不能显示已提交。

**状态/异常**：Record=Pending→Writing→Committed/Failed/Recovered；Evidence=Present/Missing/Corrupt。 SQLite写盘失败不报归档成功；两次同编号记录不得覆盖；存储缺证据需显示Unavailable；报告不可把ACK翻译成夹具到位。

**设计文件 8 项：**

- `src/AutoTest.Records/Model/RunRecord.cs` → `RunRecord,RunAttempt`；方法 `CreateFromSnapshot/Validate`；DUT、工程、软固件、测试计划指纹。
- `src/AutoTest.Records/Model/RunFact.cs` → `RunFact`；方法 `Append/ValidateSequence`；结构化步骤/操作/观测事实。
- `src/AutoTest.Records/Storage/RecordDatabase.cs` → `RecordDatabase`；方法 `Open/Migrate/CommitTransaction`；SQLite schema和事务。
- `src/AutoTest.Records/Storage/RunRecordWriter.cs` → `RunRecordWriter`；方法 `Begin/Append/CommitAsync`；追加写和提交回执。
- `src/AutoTest.Records/Storage/ArtifactRepository.cs` → `ArtifactRepository`；方法 `Put/Verify/Read`；波形、报文和烧录文件证据引用。
- `src/AutoTest.Records/Queries/RunHistoryQuery.cs` → `RunHistoryQuery`；方法 `FindByDut/FindByRun/Filter`；历史不可变只读查询。
- `src/AutoTest.Records/Queries/ProductionStatistics.cs` → `ProductionStatistics`；方法 `CountAttempts/CountDistinctDuts/GetSummary`；次数与不同ECU统计严格分离。
- `src/AutoTest.Records/Exports/RunReportExporter.cs` → `RunReportExporter`；方法 `ExportSummary/ExportStepFacts`；报告包含证据来源与Unknown。

**参考源码/资料：**

- `HistoricalHOST:src/AutoTest.Records.Sqlite/SqliteExecutionRecordStore.cs`
- `HistoricalHOST:src/AutoTest.Records/ExecutionRecordBuilder.cs`
- `HistoricalHOST:src/AutoTest.Records/FileArtifactStore.cs`

**仍需通过实验关闭：** 保留原始 CAN 与波形的容量、留存周期和压缩方案需实际样本测量后定，不编造政策。

### P1｜生产流程：扫码、烧录、测试与防遗漏

**工程：** `AutoTest.Application`；**G2：** G2-03/04/05/06/08；ICD-05/09/10

生产空间仅协同已有专业服务，不重新写 CAN/UDS/Flash/Runtime。扫码得到匹配草稿→验证 DUT 型号和物理电压→根据项目配置选择烧录和测试资源→冻结作业身份→按可编辑工作流执行→保存记录、统计和不完整提示。

**主要调用链**：ScanDraft→MatchDecision→Preflight→JobSnapshot→(受控 Flash? / TestRun)→结果记录→统计；此流程作为可配置候选，用户试用后可以调整操作顺序，不影响身份锁定及共用服务。

**状态/异常**：Empty→Scanned→Matched→Ready→Running→AwaitingCommit→Completed/Blocked/Failed；运行中二次扫描不改变当前Job。 匹配歧义、12V/24V不符或未知、烧录不可恢复均禁止自动启动；测试重复必须新RunId；遗漏和中断展示独立状态。

**设计文件 7 项：**

- `src/AutoTest.Application/Production/ProductionSession.cs` → `ProductionSession`；方法 `StartDraft/Confirm/Reset`；生产作业身份边界。
- `src/AutoTest.Application/Production/ProductionPreflight.cs` → `ProductionPreflight`；方法 `ValidateMatch/ValidateStation`；资源、目标、权限和电压检查。
- `src/AutoTest.Application/Production/ProductionWorkflow.cs` → `ProductionWorkflow`；方法 `Prepare/ExecuteAsync`；组合FlashJob和TestRun而不重复实现。
- `src/AutoTest.Application/Production/ProductionResultCoordinator.cs` → `ProductionResultCoordinator`；方法 `FinalizeAsync/RequireCommit`；检查Run/Flash/Record完成状态。
- `src/AutoTest.Application/Production/ProductionStatisticsProjection.cs` → `ProductionStatisticsProjection`；方法 `Refresh/Filter`；读取记录域统计不可私存第二份。
- `src/AutoTest.Application/Production/ProductionCommandHandler.cs` → `ProductionCommandHandler`；方法 `Scan/Start/Stop/RetryWithNewRun`；命令权限和状态反馈。
- `src/AutoTest.Desktop/Production/ProductionViewModel.cs` → `ProductionViewModel`；方法 `CaptureScan/StartWork/ShowResult`；可见扫码/匹配/进度/统计。

**参考源码/资料：**

- `CanTool:Services/FlashService.cs`
- `HistoricalHOST:src/AutoTest.Application/RunUseCases.cs`
- `HistoricalHOST:src/AutoTest.Application/RecordUseCases.cs`
- `HistoricalHOST:src/AutoTest.QuickDesktop/Operator/OperatorRunWorkspaceView.xaml.cs`

**仍需通过实验关闭：** 最终生产操作顺序、操作员权限与确认策略要使用真实工位试用后确定；当前结构提供可变流程而非写死。

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

文件/类/方法、接口、步骤类型、测试、证据和问题均在本次交付 CSV 中有稳定 ID。主索引：`tables/CROSS_MODULE_CONTRACTS.csv`。仅有正文但缺少对应表格或设计模块时，不能宣称该工作产品完成。

## Gate 限制

批准详细设计不等于通过对应单元测试、真实台架或完整生产烧录。

## 编程前必读的参考协议与接口原型

本交付附 `docs/REFERENCE_ANCHORED_PROTOCOL_CONTRACTS.md`（源码行、CRC、黄金向量）与 `docs/PUBLIC_API_SIGNATURES.md`（强类型接口候审原型），两份内容是本工作产品的配套设计约束。
